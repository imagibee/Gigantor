using System;

namespace Imagibee {
    namespace Gigantor {

        public interface IBackground
        {
            bool Running { get;  }
        }

        [Flags]
        public enum MapJoinOption {
            None = 0,
            ReducingJoins = 1,
            MultitreadedJoins = 2,
        }

        public interface IMapJoinData {
            public int Id { get; set; }
            public int Cycle { get; set; }
        }
    }
}