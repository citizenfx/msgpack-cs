using CitizenFX.MsgPack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CitizenFX.MsgPack
{
    public static class MsgPackReferenceRegistrar
    {
        public static Func<MsgPackFunc, KeyValuePair<int, byte[]>> CreateFunc { get; set; }

        public static KeyValuePair<int, byte[]> Register(MsgPackFunc func)
        {
            if (CreateFunc == null)
                throw new InvalidOperationException("Reference function not registered");

            return CreateFunc(func);
        }
    }
}
