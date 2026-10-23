using System;
using System.Windows.Forms;

namespace CP2077SaveEditor.Utils;

public static class Extensions
{
    public static void InvokeIfRequired(this ContainerControl control, Action action)
    {
        if (control.InvokeRequired)
        {
            control.Invoke(action);
        }
        else
        {
            action();
        }
    }
}