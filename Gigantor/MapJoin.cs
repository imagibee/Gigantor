using System;

namespace Imagibee {
    namespace Gigantor {

        //
        // The base class for map/join operations
        //
        // Defines an abstraction for mapping many JobT to 1 or many ResultT.
        //
        public abstract class MapJoin<JobT, ResultT> where JobT : IMapJoinData
        {
            // Called by a background worker thread to map partition data to a ResultT
            protected abstract ResultT Map(JobT data);

            // Called by a background worker thread to join a and b
            //
            // The interpretation of a, b, and return value depends on joinMode
            protected abstract ResultT Join(ResultT a, ResultT b);

            // Called in background manager thread after all Join complete,
            // override to perform final actions
            protected virtual void Finish() {}

            // Defines the join mode
            protected JoinMode joinMode;
        }

        // Defines several behaviour options for joining partitions
        public enum JoinMode {
            // No joining, Join is never called
            None,

            // Sequential join, the Map results are joined in order with feedback
            //
            // Given 5 partitions are mapped to results { A, B, C, D, E },
            // the Join sequence will be as follows:
            //
            //    A' = Join(A, A)
            //    B' = Join(A', B)
            //    C' = Join(B', C)
            //    D' = Join(C', D)
            //         Join(D', E)
            Sequential,


            // Reduce join, adjacent Map results are joined in parallel and
            // the resulting join is placed back into the results queue with an
            // increased Cycle count.  This leads to an exponential reduction
            // in results every cycle until all results are joined.
            //
            // Given 5 partitions are mapped to results { A, B, C, D, E },
            // the Join sequence will be as follows:
            //
            //    Cycle 0
            //    AB = Join(A, B)
            //    CD = Join(C, D)
            //
            //    Cycle 1
            //    ABCD = Join(AB, CD)
            //
            //    Cycle 2
            //    ABCDE = Join(ABCD, E)
            Reduce,
        }

        // Required MapJoin job properties
        public interface IMapJoinData {
            public int Id { get; set; }
            public int Cycle { get; set; }
        }

        // A default MapJoin job type for convenience
        public struct MapJoinData : IMapJoinData {
            public int Id { get; set; }
            public int Cycle { get; set; }
        }
    }
}