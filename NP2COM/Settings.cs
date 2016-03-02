using System.IO;
using System.IO.Ports;
using System.Xml.Serialization;

namespace NP2COMV
{
    public class Settings
    {
        public string MachineName { get; set; }
        public string NamedPipe { get; set; }
        public string ComPort { get; set; }
        public int BaudRate { get; set; }
        public StopBits StopBits { get; set; }
        public Parity Parity { get; set; }
        public int DataBits { get; set; }

        public void Save(string fileName)
        {
            if (File.Exists(fileName)) File.Delete(fileName);
            using (var fs = File.Open(fileName,FileMode.CreateNew))
                new XmlSerializer(typeof(Settings)).Serialize(fs, this);
        }

        public static Settings Load(string fileName)
        {
            if (File.Exists(fileName))
            {
                using (var fs =File.OpenRead(fileName))
                    return (Settings)new XmlSerializer(typeof (Settings)).Deserialize(fs);
            }
            //return new Settings();
            throw new FileNotFoundException("Can't find config file", fileName);
        }
    }
}