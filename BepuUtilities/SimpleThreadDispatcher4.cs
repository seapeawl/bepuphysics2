﻿using System;
using System.Diagnostics;
using System.Threading;
using BepuUtilities.Memory;

namespace BepuUtilities
{
    /// <summary>
    /// Provides a <see cref="IThreadDispatcher"/> implementation. Not reentrant.
    /// </summary>
    public class SimpleThreadDispatcher4 : IThreadDispatcher, IDisposable
    {
        int threadCount;
        /// <summary>
        /// Gets the number of threads to dispatch work on.
        /// </summary>
        public int ThreadCount => threadCount;
        struct Worker
        {
            public Thread Thread;
        }

        Worker[] workers;
        ManualResetEvent signalA;
        ManualResetEvent signalB;
        AutoResetEvent finished;

        BufferPool[] bufferPools;

        /// <summary>
        /// Creates a new thread dispatcher with the given number of threads.
        /// </summary>
        /// <param name="threadCount">Number of threads to dispatch on each invocation.</param>
        public SimpleThreadDispatcher4(int threadCount)
        {
            this.threadCount = threadCount;
            workers = new Worker[threadCount - 1];
            signalA = new ManualResetEvent(false);
            signalB = new ManualResetEvent(false);
            finished = new AutoResetEvent(false);
            for (int i = 0; i < workers.Length; ++i)
            {
                workers[i] = new Worker { Thread = new Thread(WorkerLoop) };
                workers[i].Thread.IsBackground = true;
                workers[i].Thread.Start(i + 1);
            }
            bufferPools = new BufferPool[threadCount];
            for (int i = 0; i < bufferPools.Length; ++i)
            {
                bufferPools[i] = new BufferPool();
            }
        }

        void DispatchThread(int workerIndex)
        {
            Debug.Assert(workerBody != null);
            Debug.Assert(remainingWorkerCounter >= 0);
            workerBody(workerIndex);

            if (Interlocked.Decrement(ref remainingWorkerCounter) == 0)
            {
                finished.Set();
            }
        }

        volatile Action<int> workerBody;
        int remainingWorkerCounter;

        void WorkerLoop(object untypedWorkerIndex)
        {
            var workerIndex = (int)untypedWorkerIndex;
            var signal = this.signalA;
            var otherSignal = this.signalB;
            while (true)
            {
                //Signals get ping ponged.
                signal.WaitOne();
                if (disposed)
                    return;
                var temp = signal;
                signal = otherSignal;
                otherSignal = temp;
                DispatchThread(workerIndex);
            }
        }

        void SignalThreads(int maximumWorkerCount)
        {
            var workersToSignal = maximumWorkerCount < threadCount ? maximumWorkerCount : threadCount;
            remainingWorkerCounter = workersToSignal;
            signalA.Set();
        }

        public void DispatchWorkers(Action<int> workerBody, int maximumWorkerCount = int.MaxValue)
        {
            if (maximumWorkerCount > 1)
            {
                Debug.Assert(this.workerBody == null);
                this.workerBody = workerBody;
                SignalThreads(maximumWorkerCount);
                //Calling thread does work. No reason to spin up another worker and block this one!
                DispatchThread(0);
                finished.WaitOne();
                signalA.Reset();
                var temp = signalA;
                signalA = signalB;
                signalB = temp;
                this.workerBody = null;
            }
            else if (maximumWorkerCount == 1)
            {
                workerBody(0);
            }
        }

        volatile bool disposed;

        /// <summary>
        /// Waits for all pending work to complete and then disposes all workers.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                SignalThreads(threadCount);
                for (int i = 0; i < bufferPools.Length; ++i)
                {
                    bufferPools[i].Clear();
                }
                foreach (var worker in workers)
                {
                    worker.Thread.Join();
                }
                signalA.Dispose();
                signalB.Dispose();
            }
        }

        public BufferPool GetThreadMemoryPool(int workerIndex)
        {
            return bufferPools[workerIndex];
        }
    }

}
