using Realms;
using System;

namespace VideoBackupper
{
    class Item : RealmObject
    {
        [Indexed]
        public string Name { get; set; }

        public DateTimeOffset LastWriteTime { get; set; }
    }
}
