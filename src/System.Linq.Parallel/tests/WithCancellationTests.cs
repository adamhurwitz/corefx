// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Test
{
    // a key part of cancellation testing is 'promptness'.  Those tests appear in pfxperfunittests.
    // the tests here are only regarding basic API correctness and sanity checking.
    public static class WithCancellationTests
    {
        [Fact]
        public static void PreCanceledToken_ForAll()
        {
            OperationCanceledException caughtException = null;
            var cs = new CancellationTokenSource();
            cs.Cancel();

            int[] srcEnumerable = Enumerable.Range(0, 1000).ToArray();
            ThrowOnFirstEnumerable<int> throwOnFirstEnumerable = new ThrowOnFirstEnumerable<int>(srcEnumerable);

            try
            {
                throwOnFirstEnumerable
                    .AsParallel()
                    .WithCancellation(cs.Token)
                    .ForAll((x) => { Console.WriteLine(x.ToString()); });
            }
            catch (OperationCanceledException ex)
            {
                caughtException = ex;
            }

            Assert.NotNull(caughtException);
            Assert.Equal(cs.Token, caughtException.CancellationToken);
        }

        [Fact]
        [ActiveIssue(240)]
        public static void PreCanceledToken_SimpleEnumerator()
        {
            OperationCanceledException caughtException = null;
            var cs = new CancellationTokenSource();
            cs.Cancel();

            int[] srcEnumerable = Enumerable.Range(0, 1000).ToArray();
            ThrowOnFirstEnumerable<int> throwOnFirstEnumerable = new ThrowOnFirstEnumerable<int>(srcEnumerable);

            try
            {
                var query = throwOnFirstEnumerable
                    .AsParallel()
                    .WithCancellation(cs.Token);

                foreach (var item in query)
                {
                }
            }
            catch (OperationCanceledException ex)
            {
                caughtException = ex;
            }

            Assert.NotNull(caughtException);
            Assert.Equal(cs.Token, caughtException.CancellationToken);
        }

        [Fact]
        public static void MultiplesWithCancellationIsIllegal()
        {
            InvalidOperationException caughtException = null;
            try
            {
                CancellationTokenSource cs = new CancellationTokenSource();
                CancellationToken ct = cs.Token;
                var query = Enumerable.Range(1, 10).AsParallel().WithDegreeOfParallelism(2).WithDegreeOfParallelism(2);
                query.ToArray();
            }
            catch (InvalidOperationException ex)
            {
                caughtException = ex;
                //Program.TestHarness.Log("IOE caught. message = " + ex.Message);
            }

            Assert.NotNull(caughtException);
        }

        [Fact]
        [ActiveIssue(176)]
        public static void CTT_Sorting_ToArray()
        {
            int size = 10000;
            CancellationTokenSource tokenSource = new CancellationTokenSource();

            Task.Run(
                () =>
                {
                    //Thread.Sleep(500);
                    tokenSource.Cancel();
                });

            OperationCanceledException caughtException = null;
            try
            {
                // This query should run for at least a few seconds due to the sleeps in the select-delegate
                var query =
                    Enumerable.Range(1, size).AsParallel()
                        .WithCancellation(tokenSource.Token)
                        .Select(
                        i =>
                        {
                            //Thread.Sleep(1);
                            return i;
                        });

                query.ToArray();
            }
            catch (OperationCanceledException ex)
            {
                caughtException = ex;
            }

            Assert.NotNull(caughtException);
            Assert.Equal(tokenSource.Token, caughtException.CancellationToken);
        }

        [Fact]
        [ActiveIssue(176)]
        public static void CTT_NonSorting_AsynchronousMergerEnumeratorDispose()
        {
            int size = 10000;
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            Exception caughtException = null;

            var query =
                    Enumerable.Range(1, size).AsParallel()
                        .WithCancellation(tokenSource.Token)
                        .Select(
                        i =>
                        {
                            //Thread.Sleep(1000);
                            return i;
                        });

            IEnumerator<int> enumerator = query.GetEnumerator();

            Task.Run(
                () =>
                {
                    //Thread.Sleep(500);
                    enumerator.Dispose();
                });

            try
            {
                // This query should run for at least a few seconds due to the sleeps in the select-delegate
                for (int j = 0; j < 1000; j++)
                {
                    enumerator.MoveNext();
                }
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            Assert.NotNull(caughtException);
        }



        [Fact]
        public static void CTT_NonSorting_SynchronousMergerEnumeratorDispose()
        {
            int size = 10000;
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            Exception caughtException = null;

            var query =
                    Enumerable.Range(1, size).AsParallel()
                        .WithCancellation(tokenSource.Token)
                        .Select(
                        i =>
                        {
                            SimulateThreadSleep(100);
                            return i;
                        }).WithMergeOptions(ParallelMergeOptions.FullyBuffered);

            IEnumerator<int> enumerator = query.GetEnumerator();

            Task.Run(
                () =>
                {
                    SimulateThreadSleep(200);
                    enumerator.Dispose();
                });

            try
            {
                // This query should run for at least a few seconds due to the sleeps in the select-delegate
                for (int j = 0; j < 1000; j++)
                {
                    enumerator.MoveNext();
                }
            }
            catch (ObjectDisposedException ex)
            {
                caughtException = ex;
            }

            Assert.NotNull(caughtException);
        }

        [Fact]
        [ActiveIssue(176)]
        public static void CTT_NonSorting_ToArray_ExternalCancel()
        {
            int size = 10000;
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            OperationCanceledException caughtException = null;

            Task.Run(
                () =>
                {
                    //Thread.Sleep(1000);
                    tokenSource.Cancel();
                });

            try
            {
                int[] output = Enumerable.Range(1, size).AsParallel()
                   .WithCancellation(tokenSource.Token)
                   .Select(
                   i =>
                   {
                       //Thread.Sleep(100);
                       return i;
                   }).ToArray();
            }
            catch (OperationCanceledException ex)
            {
                caughtException = ex;
            }

            Assert.NotNull(caughtException);
            Assert.Equal(tokenSource.Token, caughtException.CancellationToken);
        }

        /// <summary>
        /// 
        /// [Regression Test]
        ///   This issue occured because the QuerySettings structure was not being deep-cloned during 
        ///   query-opening.  As a result, the concurrent inner-enumerators (for the RHS operators)
        ///   that occur in SelectMany were sharing CancellationState that they should not have.
        ///   The result was that enumerators could falsely believe they had been canceled when 
        ///   another inner-enumerator was disposed.
        ///   
        ///   Note: the failure was intermittent.  this test would fail about 1 in 2 times on mikelid1 (4-core).
        /// </summary>
        /// <returns></returns>
        [Fact]
        public static void CloningQuerySettingsForSelectMany()
        {
            var plinq_src = ParallelEnumerable.Range(0, 1999).AsParallel();
            Exception caughtException = null;

            try
            {
                var inner = ParallelEnumerable.Range(0, 20).AsParallel().Select(_item => _item);
                var output = plinq_src
                    .SelectMany(
                        _x => inner,
                        (_x, _y) => _x
                    )
                    .ToArray();
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            Assert.Null(caughtException);
        }

        // [Regression Test]
        // Use of the async channel can block both the consumer and producer threads.. before the cancellation work
        // these had no means of being awoken.
        //
        // However, only the producers need to wake up on cancellation as the consumer 
        // will wake up once all the producers have gone away (via AsynchronousOneToOneChannel.SetDone())
        //
        // To specifically verify this test, we want to know that the Async channels were blocked in TryEnqueChunk before Dispose() is called
        //  -> this was verified manually, but is not simple to automate
        [Fact]
        public static void ChannelCancellation_ProducerBlocked()
        {
            Console.WriteLine("PlinqCancellationTests.ChannelCancellation_ProducerBlocked()");


            Console.WriteLine("        Query running (should be few seconds max)..");
            var query1 = Enumerable.Range(0, 100000000)  //provide 100million elements to ensure all the cores get >64K ints. Good up to 1600cores
                .AsParallel()
                .Select(x => x);
            var enumerator1 = query1.GetEnumerator();
            enumerator1.MoveNext();
            Task.Delay(1000).Wait();
            //Thread.Sleep(1000); // give the pipelining time to fill up some buffers.
            enumerator1.MoveNext();
            enumerator1.Dispose(); //can potentially hang

            Console.WriteLine("        Done (success).");
        }

        /// <summary>
        /// [Regression Test]
        ///   This issue occurred because aggregations like Sum or Average would incorrectly
        ///   wrap OperationCanceledException with AggregateException.
        /// </summary>
        [Fact]
        public static void AggregatesShouldntWrapOCE()
        {
            var cs = new CancellationTokenSource();
            cs.Cancel();

            // Expect OperationCanceledException rather than AggregateException or something else
            try
            {
                Enumerable.Range(0, 1000).AsParallel().WithCancellation(cs.Token).Sum(x => x);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Assert.True(false, string.Format("PlinqCancellationTests.AggregatesShouldntWrapOCE:  > Failed: got {0}, expected OperationCanceledException", e.GetType().ToString()));
            }

            Assert.True(false, string.Format("PlinqCancellationTests.AggregatesShouldntWrapOCE:  > Failed: no exception occured, expected OperationCanceledException"));
        }

        // Plinq supresses OCE(externalCT) occuring in worker threads and then throws a single OCE(ct)
        // if a manual OCE(ct) is thrown but ct is not canceled, Plinq should not suppress it, else things
        // get confusing...
        // ONLY an OCE(ct) for ct.IsCancellationRequested=true is co-operative cancellation
        [Fact]
        public static void OnlySuppressOCEifCTCanceled()
        {
            AggregateException caughtException = null;
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken externalToken = cts.Token;
            try
            {
                Enumerable.Range(1, 10).AsParallel()
                    .WithCancellation(externalToken)
                    .Select(
                      x =>
                      {
                          if (x % 2 == 0) throw new OperationCanceledException(externalToken);
                          return x;
                      }
                    )
                 .ToArray();
            }
            catch (AggregateException ae)
            {
                caughtException = ae;
            }

            Assert.NotNull(caughtException);
        }

        // a specific repro where inner queries would see an ODE on the merged cancellation token source
        // when the implementation involved disposing and recreating the token on each worker thread
        [Fact]
        public static void Cancellation_ODEIssue()
        {
            AggregateException caughtException = null;
            try
            {
                Enumerable.Range(0, 1999).ToArray()
                .AsParallel().AsUnordered()
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .Zip<int, int, int>(
                    Enumerable.Range(1000, 20).Select<int, int>(_item => (int)_item).AsParallel().AsUnordered(),
                    (first, second) => { throw new OperationCanceledException(); })
               .ForAll(x => { });
            }
            catch (AggregateException ae)
            {
                caughtException = ae;
            }

            //the failure was an ODE coming out due to an ephemeral disposed merged cancellation token source.
            Assert.True(caughtException != null,
                                              "Cancellation_ODEIssue:  We expect an aggregate exception with OCEs in it.");
        }

        [Fact]
        public static void CancellationSequentialWhere()
        {
            IEnumerable<int> src = Enumerable.Repeat(0, int.MaxValue);
            CancellationTokenSource tokenSrc = new CancellationTokenSource();

            var q = src.AsParallel().WithCancellation(tokenSrc.Token).Where(x => false).TakeWhile(x => true);

            Task task = Task.Run(
                () =>
                {
                    try
                    {
                        foreach (var x in q) { }

                        Assert.True(false, string.Format("PlinqCancellationTests.CancellationSequentialWhere:  > Failed: OperationCanceledException was not caught."));
                    }
                    catch (OperationCanceledException oce)
                    {
                        if (oce.CancellationToken != tokenSrc.Token)
                        {
                            Assert.True(false, string.Format("PlinqCancellationTests.CancellationSequentialWhere:  > Failed: Wrong cancellation token."));
                        }
                    }
                }
            );

            // We wait for 100 ms. If we canceled the token source immediately, the cancellation
            // would occur at the query opening time. The goal of this test is to test cancellation
            // at query execution time.
            Task.Delay(100).Wait();
            //Thread.Sleep(100);

            tokenSrc.Cancel();
            task.Wait();
        }

        [Fact]
        public static void CancellationSequentialElementAt()
        {
            IEnumerable<int> src = Enumerable.Repeat(0, int.MaxValue);
            CancellationTokenSource tokenSrc = new CancellationTokenSource();

            Task task = Task.Run(
                () =>
                {
                    try
                    {
                        int res = src.AsParallel()
                            .WithCancellation(tokenSrc.Token)
                            .Where(x => true)
                            .TakeWhile(x => true)
                            .ElementAt(int.MaxValue - 1);

                        Assert.True(false, string.Format("PlinqCancellationTests.CancellationSequentialElementAt:  > Failed: OperationCanceledException was not caught."));
                    }
                    catch (OperationCanceledException oce)
                    {
                        Assert.Equal(oce.CancellationToken, tokenSrc.Token);
                    }
                }
            );

            // We wait for 100 ms. If we canceled the token source immediately, the cancellation
            // would occur at the query opening time. The goal of this test is to test cancellation
            // at query execution time.
            Task.Delay(100).Wait();
            //Thread.Sleep(100);

            tokenSrc.Cancel();
            task.Wait();
        }

        [Fact]
        public static void CancellationSequentialDistinct()
        {
            IEnumerable<int> src = Enumerable.Repeat(0, int.MaxValue);
            CancellationTokenSource tokenSrc = new CancellationTokenSource();

            Task task = Task.Run(
                () =>
                {
                    try
                    {
                        var q = src.AsParallel()
                            .WithCancellation(tokenSrc.Token)
                            .Distinct()
                            .TakeWhile(x => true);

                        foreach (var x in q) { }

                        Assert.True(false, string.Format("PlinqCancellationTests.CancellationSequentialDistinct:  > Failed: OperationCanceledException was not caught."));
                    }
                    catch (OperationCanceledException oce)
                    {
                        Assert.Equal(oce.CancellationToken, tokenSrc.Token);
                    }
                }
            );

            // We wait for 100 ms. If we canceled the token source immediately, the cancellation
            // would occur at the query opening time. The goal of this test is to test cancellation
            // at query execution time.
            Task.Delay(100).Wait();
            //Thread.Sleep(100);

            tokenSrc.Cancel();
            task.Wait();
        }

        // Regression test for an issue causing ODE if a queryEnumator is disposed before moveNext is called.
        [Fact]
        [ActiveIssue(240)]
        public static void ImmediateDispose()
        {
            var queryEnumerator = Enumerable.Range(1, 10).AsParallel().Select(x => x).GetEnumerator();
            queryEnumerator.Dispose();
        }

        // REPRO 1 -- cancellation
        [Fact]
        [ActiveIssue(176)]
        public static void SetOperationsThrowAggregateOnCancelOrDispose_1()
        {
            var mre = new ManualResetEvent(false);
            var plinq_src =
                Enumerable.Range(0, 5000000).Select(x =>
                {
                    if (x == 0) mre.Set();
                    return x;
                });

            Task t = null;
            try
            {
                CancellationTokenSource cs = new CancellationTokenSource();
                var plinq = plinq_src
                    .AsParallel().WithCancellation(cs.Token)
                    .WithDegreeOfParallelism(1)
                    .Union(Enumerable.Range(0, 10).AsParallel());

                var walker = plinq.GetEnumerator();

                t = Task.Run(() =>
                {
                    mre.WaitOne();
                    cs.Cancel();
                });
                while (walker.MoveNext())
                {
                    //Thread.Sleep(1);
                    var item = walker.Current;
                }
                walker.MoveNext();
                Assert.True(false, string.Format("PlinqCancellationTests.SetOperationsThrowAggregateOnCancelOrDispose_1:  OperationCanceledException was expected, but no exception occured."));
            }
            catch (OperationCanceledException)
            {
                //This is expected.                
            }

            catch (Exception e)
            {
                Assert.True(false, string.Format("PlinqCancellationTests.SetOperationsThrowAggregateOnCancelOrDispose_1:  OperationCanceledException was expected, but a different exception occured.  " + e.ToString()));
            }

            if (t != null) t.Wait();
        }

        // throwing a fake OCE(ct) when the ct isn't canceled should produce an AggregateException.
        [Fact]
        [ActiveIssue(240)]
        public static void SetOperationsThrowAggregateOnCancelOrDispose_2()
        {
            try
            {
                CancellationTokenSource cs = new CancellationTokenSource();
                var plinq = Enumerable.Range(0, 50)
                    .AsParallel().WithCancellation(cs.Token)
                    .WithDegreeOfParallelism(1)
                    .Union(Enumerable.Range(0, 10).AsParallel().Select<int, int>(x => { throw new OperationCanceledException(cs.Token); }));

                var walker = plinq.GetEnumerator();
                while (walker.MoveNext())
                {
                    //Thread.Sleep(1);
                }
                walker.MoveNext();
                Assert.True(false, string.Format("PlinqCancellationTests.SetOperationsThrowAggregateOnCancelOrDispose_2:  failed.  AggregateException was expected, but no exception occured."));
            }
            catch (OperationCanceledException)
            {
                Assert.True(false, string.Format("PlinqCancellationTests.SetOperationsThrowAggregateOnCancelOrDispose_2:  FAILED.  AggregateExcption was expected, but an OperationCanceledException occured."));
            }
            catch (AggregateException)
            {
                // expected
            }

            catch (Exception e)
            {
                Assert.True(false, string.Format("PlinqCancellationTests.SetOperationsThrowAggregateOnCancelOrDispose_2.  failed.  AggregateExcption was expected, but some other exception occured." + e.ToString()));
            }
        }

        // Changes made to hash-partitioning (April'09) lost the cancellation checks during the 
        // main repartitioning loop (matrix building).
        [Fact]
        public static void HashPartitioningCancellation()
        {
            OperationCanceledException caughtException = null;

            CancellationTokenSource cs = new CancellationTokenSource();

            //Without ordering
            var queryUnordered = Enumerable.Range(0, int.MaxValue)
                .Select(x => { if (x == 0) cs.Cancel(); return x; })
                .AsParallel()
                .WithCancellation(cs.Token)
                .Intersect(Enumerable.Range(0, 1000000).AsParallel());

            try
            {
                foreach (var item in queryUnordered)
                {
                }
            }
            catch (OperationCanceledException oce)
            {
                caughtException = oce;
            }

            Assert.NotNull(caughtException);

            caughtException = null;

            //With ordering
            var queryOrdered = Enumerable.Range(0, int.MaxValue)
               .Select(x => { if (x == 0) cs.Cancel(); return x; })
               .AsParallel().AsOrdered()
               .WithCancellation(cs.Token)
               .Intersect(Enumerable.Range(0, 1000000).AsParallel());

            try
            {
                foreach (var item in queryOrdered)
                {
                }
            }
            catch (OperationCanceledException oce)
            {
                caughtException = oce;
            }

            Assert.NotNull(caughtException);
        }

        // If a query is cancelled and immediately disposed, the dispose should not throw an OCE.
        [Fact]
        public static void CancelThenDispose()
        {
            try
            {
                CancellationTokenSource cancel = new CancellationTokenSource();
                var q = ParallelEnumerable.Range(0, 1000).WithCancellation(cancel.Token).Select(x => x);
                IEnumerator<int> e = q.GetEnumerator();
                e.MoveNext();

                cancel.Cancel();
                e.Dispose();
            }
            catch (Exception e)
            {
                Assert.True(false, string.Format("PlinqCancellationTests.CancelThenDispose:  > Failed. Expected no exception, got " + e.GetType()));
            }
        }

        [Fact]
        [ActiveIssue(240)]
        public static void CancellationCausingNoDataMustThrow()
        {
            OperationCanceledException oce = null;

            CancellationTokenSource cs = new CancellationTokenSource();

            var query = Enumerable.Range(0, 100000000)
            .Select(x =>
            {
                if (x == 0) cs.Cancel();
                return x;
            })
            .AsParallel()
            .WithCancellation(cs.Token)
            .Select(x => x);

            try
            {
                foreach (var item in query) //We expect an OperationCancelledException during the MoveNext
                {
                }
            }
            catch (OperationCanceledException ex)
            {
                oce = ex;
            }

            Assert.NotNull(oce);
        }

        [Fact]
        public static void DontDoWorkIfTokenAlreadyCanceled()
        {
            OperationCanceledException oce = null;

            CancellationTokenSource cs = new CancellationTokenSource();
            var query = Enumerable.Range(0, 100000000)
            .Select(x =>
            {
                if (x > 0) // to avoid the "Error:unreachable code detected"
                    throw new ArgumentException("User-delegate exception.");
                return x;
            })
            .AsParallel()
            .WithCancellation(cs.Token)
            .Select(x => x);

            cs.Cancel();
            try
            {
                foreach (var item in query) //We expect an OperationCancelledException during the MoveNext
                {
                }
            }
            catch (OperationCanceledException ex)
            {
                oce = ex;
            }

            Assert.NotNull(oce);
        }

        // To help the user, we will check if a cancellation token passed to WithCancellation() is 
        // not backed by a disposed CTS.  This will help them identify incorrect cts.Dispose calls, but 
        // doesn't solve all their problems if they don't manage CTS lifetime correctly.
        // We test via a few random queries that have shown inconsistent behaviour in the past.
        [Fact]
        public static void PreDisposedCTSPassedToPlinq()
        {
            ArgumentException ae1 = null;
            ArgumentException ae2 = null;
            ArgumentException ae3 = null;

            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;
            cts.Dispose();  // Early dispose
            try
            {
                Enumerable.Range(1, 10).AsParallel()
                    .WithCancellation(ct)
                    .OrderBy(x => x)
                    .ToArray();
            }
            catch (Exception ex)
            {
                ae1 = (ArgumentException)ex;
            }

            try
            {
                Enumerable.Range(1, 10).AsParallel()
                    .WithCancellation(ct)
                    .Last();
            }
            catch (Exception ex)
            {
                ae2 = (ArgumentException)ex;
            }

            try
            {
                Enumerable.Range(1, 10).AsParallel()
                    .WithCancellation(ct)
                    .OrderBy(x => x)
                    .Last();
            }
            catch (Exception ex)
            {
                ae3 = (ArgumentException)ex;
            }

            Assert.NotNull(ae1);
            Assert.NotNull(ae2);
            Assert.NotNull(ae3);
        }

        public static void SimulateThreadSleep(int milliseconds)
        {
            ManualResetEvent mre = new ManualResetEvent(false);
            mre.WaitOne(milliseconds);
        }
    }

    // ---------------------------
    // Helper classes
    // ---------------------------

    internal class ThrowOnFirstEnumerable<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _innerEnumerable;

        public ThrowOnFirstEnumerable(IEnumerable<T> innerEnumerable)
        {
            _innerEnumerable = innerEnumerable;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new ThrowOnFirstEnumerator<T>(_innerEnumerable.GetEnumerator());
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    internal class ThrowOnFirstEnumerator<T> : IEnumerator<T>
    {
        private IEnumerator<T> _innerEnumerator;

        public ThrowOnFirstEnumerator(IEnumerator<T> sourceEnumerator)
        {
            _innerEnumerator = sourceEnumerator;
        }

        public void Dispose()
        {
            _innerEnumerator.Dispose();
        }

        public bool MoveNext()
        {
            throw new InvalidOperationException("ThrowOnFirstEnumerator throws on the first MoveNext");
        }

        public T Current
        {
            get { return _innerEnumerator.Current; }
        }

        object IEnumerator.Current
        {
            get { return Current; }
        }

        public void Reset()
        {
            _innerEnumerator.Reset();
        }
    }
}
