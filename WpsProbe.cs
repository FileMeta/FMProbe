using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;


namespace FMProbe
{

    /// <summary>
    /// Probes files for metadata using the Windows Property System (WPS)
    /// </summary>
    class WpsProbe
    {
        String mFilename;
        TextWriter mOut;

        public WpsProbe(String filename, TextWriter output)
        {
            mFilename = filename;
            mOut = output;
        }

        public void Probe()
        {
            using (WinShell.PropertySystem propsys = new WinShell.PropertySystem())
            {
                using (WinShell.PropertyStore store = WinShell.PropertyStore.Open(mFilename))
                {
                    int count = store.Count;
                    for (int i=0; i<count; ++i)
                    {
                        WinShell.PROPERTYKEY key = store.GetAt(i);

                        string name;
                        try
                        {
                            using (WinShell.PropertyDescription desc = propsys.GetPropertyDescription(key))
                            {
                                name = string.Concat(desc.CanonicalName, " ", desc.DisplayName);
                            }
                        }
                        catch
                        {
                            name = string.Format("({0}:{1})", key.fmtid, key.pid);
                        }

                        object value = store.GetValue(key);
                        string strValue;
                    
                        if (value is string[])
                        {
                            strValue = string.Join(";", (string[])value);
                        }
                        else
                        {
                            strValue = value.ToString();
                        }
                        Console.WriteLine("{0}: {1}", name, strValue);
                    }
                }
            }
        }
    }
}