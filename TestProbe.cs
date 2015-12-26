using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace FMProbe
{
    class TestProbe
    {
        string mFilename;
        TextWriter mOut;

        public TestProbe(string filename, TextWriter output)
        {
            mFilename = filename;
            mOut = output;
        }

        public void Probe()
        {
            using (WinShell.PropertySystem propsys = new WinShell.PropertySystem())
            {
                using (WinShell.PropertyStore store = WinShell.PropertyStore.Open(mFilename, true))
                {
                    SetProperty(propsys, store, "System.Music.TrackNumber", 4);
                    SetProperty(propsys, store, "System.Media.ContentID", Guid.NewGuid());
                    SetProperty(propsys, store, "System.OriginalFileName", "-OriginalFileName.txt");
                    //SetProperty(propsys, store, "System.Author", string.Format("TestAuthor {0}", DateTime.Now.Ticks));
                    //SetProperty(propsys, store, "System.OriginalFileName", "ofn1234.bin");
                    //SetProperty(propsys, store, "System.Media.DateReleased", DateTime.Now);
                    //SetProperty(propsys, store, "System.Media.Year", 2010);
                    //SetProperty(propsys, store, "System.Title", string.Format("TestTitle {0}", DateTime.Now.Ticks));
                    //SetProperty(propsys, store, "System.Keywords", new string[] { "Hello", "World" });
                    store.Commit();
                }
            }
        }

        void SetProperty(WinShell.PropertySystem propsys, WinShell.PropertyStore store, string canonicalName, object value)
        {
            mOut.Write("Setting {0} to '{1}'...", canonicalName, value);
            try
            {
                using (WinShell.PropertyDescription propDesc = propsys.GetPropertyDescriptionByName(canonicalName))
                {
                    store.SetValue(propDesc.PropertyKey, value);
                }
            }
            catch(Exception err)
            {
                mOut.WriteLine();
                mOut.WriteLine(err.ToString());
                mOut.WriteLine();
                return;
            }

            mOut.WriteLine("Success.");
        }

    }
}
