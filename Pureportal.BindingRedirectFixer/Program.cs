using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Pureportal.BindingRedirectFixer
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("This console fixes the assembly binding redirects of app.config or web.config");
            string binFolder = null;
            string config = null;
            bool autoMode = false;
            string solutionfolder = null;

            if(args.Length > 0 && (args.Contains("--help") || args.Contains("-h")))
            {
                Console.WriteLine("You can user --automode + directory to change all app.config easy");
                return;
            }
            else if (args.Length == 2 && args[0] == "--automode")
            {
                autoMode = true;
                solutionfolder = args[1];
            }
            else if (args.Length == 2)
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

            if (autoMode)
            {
                if (!Directory.Exists(solutionfolder))
                {
                    Console.WriteLine($"Directory {solutionfolder} does not exist");
                }
                else
                {
                    var configInfoLocations = GetConfigInfoLocations(solutionfolder);
                    foreach (var configInfoLocation in configInfoLocations)
                    {
                        UpdateAppConfig(configInfoLocation.PathToConfigFile, configInfoLocation.PathToBin);
                    }
                }
                
            }
            else
            {
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
                    UpdateAppConfig(config, binFolder);
                }
            }
        }

        private static List<ConfigInfoLocation> GetConfigInfoLocations(string solutionDir)
        {
            List<ConfigInfoLocation> configInfoLocations = new List<ConfigInfoLocation>();
            //Get subdirs
            string[] directorys = Directory.GetDirectories(solutionDir);
            foreach (var directory in directorys)
            {
                var files = Directory.GetFiles(directory, "app.config").ToList();
                files.AddRange(Directory.GetFiles(directory, "web.config").ToList());
                if(!files.Any())continue;

                List<ConfigInfoLocation> configInfoLocationsTmp = new List<ConfigInfoLocation>();

                foreach (var file in files)
                {
                    configInfoLocationsTmp.Add(new ConfigInfoLocation()
                    {
                        PathToConfigFile = file
                    });
                }
                
                //Try to load config
                var projectFiles = Directory.GetFiles(directory, "*.csproj").ToList();
                if (projectFiles.Any())
                {
                    string outPutPath = null;
                    XDocument xmldoc = XDocument.Parse(File.ReadAllText(projectFiles.First()));
                    var ns = xmldoc.Root.Name.Namespace;
                    //Open file with xml
                    XElement debugElement = xmldoc.Descendants().Where(x=>x.HasAttributes && x.FirstAttribute.Name == "Condition" && x.FirstAttribute.Value.Contains("Debug")).FirstOrDefault();
                    if (debugElement != null && debugElement.HasElements)
                    {
                        outPutPath = debugElement.Elements().Where(t => t.Name.LocalName == "OutputPath").FirstOrDefault()?.Value;
                    }

                    if (!string.IsNullOrEmpty(outPutPath) && outPutPath.Contains(":") && Directory.Exists(outPutPath))
                    {
                        configInfoLocationsTmp.ForEach(t=>t.PathToBin = outPutPath);
                    }
                    else if (!string.IsNullOrEmpty(outPutPath) && Directory.Exists(directory + "\\" + outPutPath))
                    {
                        configInfoLocationsTmp.ForEach(t=>t.PathToBin = directory + "\\" + outPutPath);
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    if (Directory.Exists(directory + "/bin/debug"))
                    {
                        configInfoLocationsTmp.ForEach(t=>t.PathToBin = directory + "/bin/debug");
                    }
                    else
                    {
                        continue;
                    }
                }
                
                configInfoLocations.AddRange(configInfoLocationsTmp);
            }

            return configInfoLocations;
        }
        
        private static void UpdateAppConfig(string config, string binFolder)
        {
            Console.WriteLine($"Patching file {config}");
            XmlDataDocument xmldoc = new XmlDataDocument();
            //Open file with xml
            using (FileStream fs = new FileStream(config, FileMode.Open, FileAccess.Read))
            {
                xmldoc.Load(fs);
            }

            //Remove binding node
            XmlNamespaceManager ns = new XmlNamespaceManager(xmldoc.NameTable);
            ns.AddNamespace("ab", "urn:schemas-microsoft-com:asm.v1");
            XmlNode assemblyBinding = xmldoc.SelectSingleNode("configuration/runtime/ab:assemblyBinding", ns);
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
            string[] files = Directory.GetFiles(binFolder, "*.dll");
            foreach (var file in files)
            {
                Console.WriteLine($"Loading dll file {file}");
                var assembly = Assembly.LoadFile(file);
                string assemblyVersion = assembly.GetName().Version.ToString();
                string assemblyName = assembly.GetName().Name;
                string publicKeyToken = GetPublicKeyTokenFromAssembly(assembly);

                XmlElement tmpAssemblyBinding = xmldoc.CreateElement("dependentAssembly");
                XmlElement tmpAssemblyIdentity = xmldoc.CreateElement("assemblyIdentity");
                tmpAssemblyIdentity.SetAttribute("name", assemblyName);
                if (!string.IsNullOrEmpty(publicKeyToken)) tmpAssemblyIdentity.SetAttribute("publicKeyToken", publicKeyToken);
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

    public class ConfigInfoLocation
    {
        public string PathToConfigFile { get; set; }
        public string PathToBin { get; set; }
    }
}