namespace Imagibee {
    namespace Gigantor {

        public interface IBackground
        {
            bool Running { get;  }
            void Start();
        }

        // JoinMode defines several modes for solving various problems
        public enum JoinMode {
            // No joining, Join is never called
            None,

            // Linear join, the Map results are joined in order with feedback
            //
            // Given 5 partitions are mapped to results { A, B, C, D, E },
            // the Join sequence will be as follows:
            //
            //    A' = Join(A, A)
            //    B' = Join(A', B)
            //    C' = Join(B', C)
            //    D' = Join(C', D)
            //         Join(D', E)
            Linear,


            // Exponential join, adjacent Map results are joined in parallel and
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
            Exponential,
        }

        public interface IMapJoinData {
            public int Id { get; set; }
            public int Cycle { get; set; }
        }

        public struct MapJoinData : IMapJoinData {
            public int Id { get; set; }
            public int Cycle { get; set; }
        }
    }
}