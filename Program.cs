using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MinaDesk {
    static class Program {
        [STAThread]
        static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool first;
            using (var mutex = new Mutex(true, @"Local\MinaDesk.WorkstationHub.v1", out first))
            using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MinaDesk.WorkstationHub.Activate.v1")) {
                if (!first) { activate.Set(); return; }
                try {
                    string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinaDesk", "WorkstationHub");
                    using (var hub = new Hub(data, activate)) Application.Run(hub);
                } catch (Exception error) {
                    MessageBox.Show("작업실을 시작할 수 없습니다.\n" + error.Message, "Mini Workstation Hub", MessageBoxButtons.OK, MessageBoxIcon.Error);
                } finally { mutex.ReleaseMutex(); }
            }
        }
    }
}

