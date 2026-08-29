using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace QuickTranslate
{
    internal sealed class ClipboardSnapshot
    {
        private sealed class Entry
        {
            public string Format;
            public object Data;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public static ClipboardSnapshot Capture()
        {
            ClipboardSnapshot snapshot = new ClipboardSnapshot();
            IDataObject source = Retry<IDataObject>(delegate { return Clipboard.GetDataObject(); });
            if (source == null) return snapshot;

            string[] formats;
            try { formats = source.GetFormats(false); }
            catch { return snapshot; }

            foreach (string format in formats)
            {
                try
                {
                    object data = source.GetData(format, false);
                    if (data != null)
                    {
                        snapshot.entries.Add(new Entry { Format = format, Data = data });
                    }
                }
                catch
                {
                }
            }
            return snapshot;
        }

        public void Restore()
        {
            if (entries.Count == 0)
            {
                Retry(delegate { Clipboard.Clear(); });
                return;
            }

            DataObject target = new DataObject();
            foreach (Entry entry in entries)
            {
                try { target.SetData(entry.Format, false, entry.Data); }
                catch { }
            }
            Retry(delegate { Clipboard.SetDataObject(target, true); });
        }

        public static void Clear()
        {
            Retry(delegate { Clipboard.Clear(); });
        }

        public static string GetText()
        {
            return Retry<string>(delegate
            {
                return Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    ? Clipboard.GetText(TextDataFormat.UnicodeText)
                    : null;
            });
        }

        public static void SetText(string value)
        {
            Retry(delegate { Clipboard.SetText(value, TextDataFormat.UnicodeText); });
        }

        private static T Retry<T>(Func<T> action)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try { return action(); }
                catch (Exception error)
                {
                    last = error;
                    Thread.Sleep(25 + attempt * 15);
                }
            }
            if (last != null) throw last;
            return default(T);
        }

        private static void Retry(Action action)
        {
            Retry<object>(delegate
            {
                action();
                return null;
            });
        }
    }
}
