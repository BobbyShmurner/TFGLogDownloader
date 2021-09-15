using System;
using System.Collections.Generic;
using System.Text;

namespace LogDownloader
{
    public class AdbArgs
    {
        public string Args;
        public string WorkingDirectory = Environment.CurrentDirectory;
        public string OutputText;

        public object ExtraArg;
        public Action<AdbArgs, object> CompleteFunction;
    }
}
