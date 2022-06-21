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
            bool nuspecMode = false;
            string solutionfolder = null;

            if(args.Length > 0 && (args.Contains("--help") || args.Contains("-h")))
            {
                Console.WriteLine("You can user --nuspec + directory to change all app.config easy");
                return;
            }
            
            if (args.Length == 2 && args[0].ToLower() == "--nuspec")
            {
                nuspecMode = true;
                solutionfolder = args[0];
            }
            else if (args.Length == 1)
            {
                autoMode = true;
                solutionfolder = args[0];
            }
            else if (args.Length == 2 && args[0].ToLower() == "--automode")
            {
                autoMode = true;
                solutionfolder = args[1];
            }
            else if (args.Length == 2)
            {
                binFolder = args[0];
                config = args[1];
            }

            if(nuspecMode)
            {
                if (!Directory.Exists(solutionfolder))
                {
                    Console.WriteLine($"Directory {solutionfolder} does not exist");
                }
                else
                {
                    List<ConfigInfoLocation> configInfoLocations = new List<ConfigInfoLocation>();
                    GetDirectorysSolution(solutionfolder,configInfoLocations);
                    
                }
            }
            if (autoMode)
            {
                if (!Directory.Exists(solutionfolder))
                {
                    Console.WriteLine($"Directory {solutionfolder} does not exist");
                }
                else
                {
                    List<ConfigInfoLocation> configInfoLocations = new List<ConfigInfoLocation>();
                    GetDirectorysSolution(solutionfolder,configInfoLocations);
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
        
        private static void GetDirectorysSolution(string directory,List<ConfigInfoLocation> configInfoLocations)
        {
            string[] directorys = Directory.GetDirectories(directory);
            foreach (var dir in directorys)
            {
                if (dir == "bin" || dir == "obj" || dir == "publish") continue;
                GetDirectorysSolution(dir, configInfoLocations);
                
                var files = Directory.GetFiles(dir, "app.config").ToList();
                files.AddRange(Directory.GetFiles(dir, "web.config").ToList());
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
                var projectFiles = Directory.GetFiles(dir, "*.csproj").ToList();
                if (projectFiles.Any())
                {
                    string outPutPath = null;
                    try
                    {
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
                        else if (!string.IsNullOrEmpty(outPutPath) && Directory.Exists(dir + "\\" + outPutPath))
                        {
                            configInfoLocationsTmp.ForEach(t=>t.PathToBin = dir + "\\" + outPutPath);
                        }
                        else
                        {
                            continue;
                        }
                    }
                    catch (Exception e)
                    {
                        
                    }
                }
                else
                {
                    if (Directory.Exists(directory + "/bin/debug"))
                    {
                        configInfoLocationsTmp.ForEach(t=>t.PathToBin = dir + "/bin/debug");
                    }
                    else
                    {
                        continue;
                    }
                }
                
                configInfoLocations.AddRange(configInfoLocationsTmp);
            }
        }

        private static void UpdateAppConfig(string config, string binFolder)
        {
            if (string.IsNullOrEmpty(binFolder)) return;
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
                Assembly assembly = null;
                Console.WriteLine($"Loading dll file {file}");
                try
                {
                    assembly = Assembly.LoadFile(file);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    continue;
                }
                
                
                
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