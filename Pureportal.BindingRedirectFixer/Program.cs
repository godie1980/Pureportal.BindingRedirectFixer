using System;
using System.IO;
using System.Reflection;
using System.Xml;

namespace Pureportal.BindingRedirectFixer
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("This console fixes the assembly binding redirects of app.config or web.config");
            string binFolder = null;
            string config = null;
            if (args.Length == 2)
            {
                binFolder = args[0];
                config = args[1];
            }
            else
            {
                Console.WriteLine("Please give full path to bin folder");
                binFolder = Console.ReadLine();
                Console.WriteLine("Please give full path to web.config or app.config");
                config = Console.ReadLine();
            }
            

            if (!File.Exists(config))
            {
                Console.WriteLine($"File {config} does not exist");
            }
            else if (!Directory.Exists(binFolder))
            {
                Console.WriteLine($"Directory {binFolder} does not exist");
            }
            else
            {
                XmlDataDocument xmldoc = new XmlDataDocument();
                //Open file with xml
                using (FileStream fs = new FileStream(config, FileMode.Open, FileAccess.Read))
                {
                    xmldoc.Load(fs);    
                }
                
                //Remove binding node
                XmlNamespaceManager ns = new XmlNamespaceManager(xmldoc.NameTable);
                ns.AddNamespace("ab", "urn:schemas-microsoft-com:asm.v1");
                XmlNode assemblyBinding = xmldoc.SelectSingleNode("configuration/runtime/ab:assemblyBinding",ns);
                if (assemblyBinding != null)
                {
                    XmlNode tmpruntime = xmldoc.SelectSingleNode("configuration/runtime");
                    tmpruntime.RemoveChild(assemblyBinding);
                }
                else
                {
                    XmlNode tmpruntime = xmldoc.SelectSingleNode("configuration/runtime");
                    if (tmpruntime == null)
                    {
                        XmlNode configuration = xmldoc.SelectSingleNode("configuration");
                        XmlElement runtimeElement = xmldoc.CreateElement("runtime");
                        configuration.AppendChild(runtimeElement);
                    }
                }
                
                XmlNode runtime = xmldoc.SelectSingleNode("configuration/runtime");
                
                XmlElement assemblyBindingElement = xmldoc.CreateElement("assemblyBinding");
                assemblyBindingElement.SetAttribute("xmlns", "urn:schemas-microsoft-com:asm.v1");
                
                //Lets create the assembly's, first load all assembly
                string[] files = Directory.GetFiles(binFolder,"*.dll");
                foreach (var file in files)
                {
                    var assembly = Assembly.LoadFile(file);
                    string assemblyVersion = assembly.GetName().Version.ToString();
                    string assemblyName = assembly.GetName().Name;
                    string publicKeyToken = GetPublicKeyTokenFromAssembly(assembly);
                    
                    XmlElement tmpAssemblyBinding = xmldoc.CreateElement("dependentAssembly");
                    XmlElement tmpAssemblyIdentity = xmldoc.CreateElement("assemblyIdentity");
                    tmpAssemblyIdentity.SetAttribute("name", assemblyName);
                    if(!string.IsNullOrEmpty(publicKeyToken))tmpAssemblyIdentity.SetAttribute("publicKeyToken", publicKeyToken);
                    XmlElement tmpBindingRedirect = xmldoc.CreateElement("bindingRedirect");
                    tmpBindingRedirect.SetAttribute("oldVersion", $"0.0.0.0-{assemblyVersion}");
                    tmpBindingRedirect.SetAttribute("newVersion", assemblyVersion);
                    tmpAssemblyBinding.AppendChild(tmpAssemblyIdentity);
                    tmpAssemblyBinding.AppendChild(tmpBindingRedirect);
                    assemblyBindingElement.AppendChild(tmpAssemblyBinding);
                }

                runtime.AppendChild(assemblyBindingElement);
                xmldoc.Save(config);
            }
        }
        private static string GetPublicKeyTokenFromAssembly(Assembly assembly)
        {
            var bytes = assembly.GetName().GetPublicKeyToken();
            if (bytes == null || bytes.Length == 0)
                return null;

            var publicKeyToken = string.Empty;
            for (int i = 0; i < bytes.GetLength(0); i++)
                publicKeyToken += string.Format("{0:x2}", bytes[i]);

            return publicKeyToken;
        }
        
    }
}