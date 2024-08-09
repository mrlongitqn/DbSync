using System.Collections.Generic;

namespace DbSync
{
    class ChangeInfo
    {
        public long Version { get; set; }
        public List<Change> Changes { get; private set; } = new List<Change>();
    }
}