using System;

namespace Imagibee {
    namespace Gigantor {
        [Flags]
        public enum MapJoinOption {
            None = 0,
            ReducingJoins = 1,
        }

        public interface IMapJoinData {
            public int Id { get; set; }
            public int Cycle { get; set; }
        }
    }
}