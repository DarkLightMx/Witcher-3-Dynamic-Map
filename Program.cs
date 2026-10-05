using System;
using System.Windows.Forms;

namespace Witcher_3_Dynamic_Map;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}
