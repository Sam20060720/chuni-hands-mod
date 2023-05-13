using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Security.Principal;

namespace chuni_hands {
    internal static class ChuniIO {

        public static MemoryMappedFile sharedBuffer;
        public static MemoryMappedViewAccessor sharedBufferAccessor;

        private static bool _init = false;
        private static readonly int[] SensorMap = new int[] { 1, 0, 3, 2, 5, 4 };
        public static void Send(IList<Sensor> sensors) {
            if (!_init) {
                Initialize();
            }


            var data = new byte[6];
            for (var i = 0; i < 6; ++i) {
                data[SensorMap[i]] = (byte)(sensors[i].Active ? 1 : 0);
            }


            sharedBufferAccessor.WriteArray<byte>(0, data, 0, 6);


            //byte[] readarr = new byte[50];
            //sharedBufferAccessor.ReadArray<byte>(0, readarr, 0, 49);
            //for (int bufi = 0; bufi <= 6; bufi++) {
            //    Console.Write(readarr[bufi]);
            //}
            //Console.WriteLine();

        }

        private static void Initialize() {
            MemoryMappedFileSecurity CustomSecurity = new MemoryMappedFileSecurity();
            SecurityIdentifier sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var acct = sid.Translate(typeof(NTAccount)) as NTAccount;
            CustomSecurity.AddAccessRule(new System.Security.AccessControl.AccessRule<MemoryMappedFileRights>(acct.ToString(), MemoryMappedFileRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            sharedBuffer = MemoryMappedFile.CreateOrOpen("Local\\BROKENITHM_SHARED_BUFFER", 1024, MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.None, CustomSecurity, System.IO.HandleInheritability.Inheritable);
            sharedBufferAccessor = sharedBuffer.CreateViewAccessor();
            _init = true;
        }
    }
}
