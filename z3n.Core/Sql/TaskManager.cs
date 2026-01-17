using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZennoLab.CommandCenter;
using ZennoLab.InterfacesLibrary.ProjectModel;

namespace z3nCore
{
    public static class TaskManager
    {
        private static string _settingsTable = "___settings";
        private static string _tasksTable = "___tasks";
        
        private static Dictionary<string, string> SettingsForDb( string taskXml)
        {
            XDocument doc = XDocument.Parse(taskXml);

            var settingsDict = new Dictionary<string, string>();

            foreach (var setting in doc.Descendants("InputSetting"))
            {
                var outputVar = setting.Element("OutputVariable")?.Value;
                var value = setting.Element("Value")?.Value ?? "";

                if (!string.IsNullOrWhiteSpace(outputVar))
                {
                    var cleanVar = outputVar.Replace("{-Variable.", "").Replace("-}", "");
                    settingsDict[cleanVar] = value;
                }
            }

            settingsDict["settings_xml"] = taskXml.ToBase64();
            return settingsDict;

        }
        private static string LoadTaskSettings(IZennoPosterProjectModel project, string taskId)
        {
            var xmlBase64 = project.DbGet("settings_xml", _settingsTable, where: $"task_id = '{taskId}'");
            var xml = xmlBase64.FromBase64();

            XDocument doc = XDocument.Parse(xml);
            
            foreach (var setting in doc.Descendants("InputSetting"))
            {
                var outputVar = setting.Element("OutputVariable")?.Value;

                if (!string.IsNullOrWhiteSpace(outputVar))
                {
                    var cleanVar = outputVar.Replace("{-Variable.", "").Replace("-}", "");


                    var dbValue = project.DbGet(cleanVar, _settingsTable, where: $"task_id = '{taskId}'");

                    if (!string.IsNullOrWhiteSpace(dbValue))
                    {
                        setting.Element("Value").Value = dbValue;
                    }
                }
            }
            return doc.ToString();
        }
        private static void LoadAllSettings(IZennoPosterProjectModel project)
        {
            var taskList = project.DbGetLines("Id", _settingsTable, where:$"\"Id\" != ''");
            foreach (var task in taskList)
            {
                var settingsFromDb = LoadTaskSettings(project, task);
                var Id = new Guid(task.ToString());
                ZennoPoster.ImportInputSettings(Id, settingsFromDb);
            }
        }
        private static void SaveAllSettings(IZennoPosterProjectModel project)
        {
            project.ClmnAdd("Id", _settingsTable);
            project.ClmnAdd("Name", _settingsTable);
            int i = 0;
            var taskList = project.DbGetLines("Id", "_tasks", where:$"\"Id\" != ''");
            foreach (var task in taskList)
            { 
                i++;
                var name = project.DbGet("Name", $"_tasks", where:$"\"Id\" = '{task}'");
                project.DbUpd($"Id = '{task}', Name = '{name}'",_settingsTable, log: true,where:$"id = {i}");
                
                var Id = new Guid(task.ToString());
                var settings = ZennoPoster.ExportInputSettings(Id);
                try
                {
                    var settingsDic = SettingsForDb(settings);
                    project.DicToDb(settingsDic, _settingsTable, log: true, where: $"id = {i}");
                }
                catch
                {
                    project.warn(settings);
                }
            }
            project.ClmnPrune(tblName:_settingsTable);

        }
        public static void UpdTasks(IZennoPosterProjectModel project)
        {
            int i = 0;
            foreach (var task in ZennoPoster.TasksList)
            {
                i++;
                string xml = "<root>" + task + "</root>";
                XDocument doc = XDocument.Parse(xml);
                string json = JsonConvert.SerializeXNode(doc);
                
                var jObj = JObject.Parse(json)["root"];
                string cleanJson = jObj.ToString();
                project.JsonToDb(cleanJson, _tasksTable, log:true, where: $"id = '{i}'");
            }
        }

        public static void TasksToDb(IZennoPosterProjectModel project, bool updTasks = false)
        {
            if (updTasks)UpdTasks(project);
            SaveAllSettings(project);
        }
        public static void TasksFromDb(IZennoPosterProjectModel project, bool updTasks = false)
        {
            if (updTasks)UpdTasks(project);
            LoadAllSettings(project);
        }

    }
}