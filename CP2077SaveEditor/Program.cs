using System;
using System.Windows.Forms;
using CP2077SaveEditor.Views;

namespace CP2077SaveEditor
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            UserPrompt.Current = new MessageBoxUserPrompt();
            Application.Run(new Form2());
        }
    }

    internal sealed class MessageBoxUserPrompt : IUserPrompt
    {
        public void Notice(string message, string title) => MessageBox.Show(message, title);

        public bool Confirm(string message, string title) =>
            MessageBox.Show(message, title, MessageBoxButtons.YesNo) == DialogResult.Yes;
    }
}
