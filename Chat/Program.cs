using System;
using System.Windows.Forms;

namespace AION.Chat
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new Chat());
        }
    }
}
