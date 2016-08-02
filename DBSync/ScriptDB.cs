using System;

using Microsoft.SqlServer.Management.Smo;
using System.Collections.Specialized;
using System.Text;
using FinLib.Logger;
using System.Collections;
using System.Collections.Generic;

namespace DBSync
{
    public static class ScriptDB
    {
        public static StringCollection ScriptProcedure(Server myServer, string database, string procedure)
        {
            StringCollection scripts = new StringCollection();

            ScriptingOptions options = new ScriptingOptions();
            options.ScriptSchema = true;
            options.IncludeIfNotExists = true;
            options.AllowSystemObjects = false;
            
            Database myDb = myServer.Databases[database];
            
            StoredProcedureCollection dbProcedures = myDb.StoredProcedures;

            if (dbProcedures.Contains(procedure))
            {
                //drop
                options.ScriptDrops = true;

                script<StoredProcedure>(dbProcedures[procedure], options, scripts);
                //create
                options.ScriptDrops = false;
                script<StoredProcedure>(dbProcedures[procedure], options, scripts);
            }

            return scripts;
        }

        public static string ScriptFunction(Server myServer, string database, string function)
        {
            ScriptingOptions options = new ScriptingOptions();
            options.ScriptSchema = true;
            options.IncludeIfNotExists = true;
            options.AllowSystemObjects = false;

            Database myDb = myServer.Databases[database];
            StringBuilder scriptBuilder = new StringBuilder();
            UserDefinedFunctionCollection dbFunctions = myDb.UserDefinedFunctions;

            if (dbFunctions.Contains(function))
            {
                //drop
                options.ScriptDrops = true;
                
                script<UserDefinedFunction>(dbFunctions[function], options, scriptBuilder);
                //create
                options.ScriptDrops = false;
                script<UserDefinedFunction>(dbFunctions[function], options, scriptBuilder);
            }

            return scriptBuilder.ToString();
        }

        public static string ScriptViews(Server myServer, string database, List<string> views)
        {
            ScriptingOptions options = new ScriptingOptions();
            
            options.ScriptSchema = true;
            options.IncludeIfNotExists = true; 
            options.AllowSystemObjects = false;
           
            Database myDb = myServer.Databases[database];

            StringBuilder scriptBuilder = new StringBuilder();

            ViewCollection dbViews = myDb.Views;
            
            foreach(string view in views)
            {
                if (dbViews.Contains(view)){
                    //drop
                    options.ScriptDrops = true;
                    script<View>(dbViews[view], options, scriptBuilder);
                    //create
                    options.ScriptDrops = false;
                    script<View>(dbViews[view], options, scriptBuilder);
                }
            }

            return scriptBuilder.ToString();
        }

        public static string ScriptView(Server myServer, string database, string view)
        {
            ScriptingOptions options = new ScriptingOptions();
            options.ScriptSchema = true;
            options.IncludeIfNotExists = true;
            options.AllowSystemObjects = false;

            Database myDb = myServer.Databases[database];
            StringBuilder scriptBuilder = new StringBuilder();
            ViewCollection dbViews = myDb.Views;

            if (dbViews.Contains(view))
            {
                //drop
                options.ScriptDrops = true;
                script<View>(dbViews[view], options, scriptBuilder);
                //create
                options.ScriptDrops = false;
                script<View>(dbViews[view], options, scriptBuilder);
            }

            return scriptBuilder.ToString();
        }

        public static Dictionary<string, TableScript> ScriptTables(Server myServer, string database, List<string> tableNames)
        {
            Dictionary<string, TableScript> tableScripts = new Dictionary<string, TableScript>();

            ScriptingOptions options = new ScriptingOptions();

            options.ScriptSchema = true;
            options.IncludeIfNotExists = true;
            options.AllowSystemObjects = false;

            Database myDb = myServer.Databases[database];

            TableCollection dbTables = myDb.Tables;

            foreach (string table in tableNames)
            {
                if (dbTables.Contains(table))
                {
                    tableScripts.Add(table, new TableScript(dbTables[table], options));
                }
            }

            return tableScripts;
        }

        public class TableScript
        {
            public readonly string name;
            private StringBuilder dropBuilder;
            private StringBuilder createBuilder;
            public TableScript(Table table, ScriptingOptions options)
            {
                this.name = table.Name;
                dropBuilder = new StringBuilder();
                options.ScriptDrops = true;
                script<Table>(table, options, dropBuilder);
                
                //create
                createBuilder = new StringBuilder();
                options.ScriptDrops = false;
                script<Table>(table, options, createBuilder);
            }

            public string drop
            {
                get
                {
                    return dropBuilder.ToString();
                }
            }

            public string create
            {
                get
                {
                    return createBuilder.ToString();
                }
            }
        }

        public static string ScriptDatabase(Server myServer, string database, bool skipSystemObjects = true)
        {
            ScriptingOptions options = new ScriptingOptions();
            //scripter.Options.ScriptDrops = true;
            options.ScriptSchema = true;
            options.IncludeIfNotExists = true;
            options.AllowSystemObjects = !skipSystemObjects;
            
            Database myDb = myServer.Databases[database];

            //List<SqlSmoObject> smoObjects = new List<SqlSmoObject>();

            //smoObjects.Add(myDb);
            //myDb.Tables.AddTo(smoObjects);
            //myDb.StoredProcedures.AddTo(smoObjects);
            //myDb.Views.AddTo(smoObjects);
            //myDb.Users.AddTo(smoObjects);
            //myDb.UserDefinedFunctions.AddTo(smoObjects);

            StringBuilder scriptBuilder = new StringBuilder();
            script(myDb, options, scriptBuilder);
            script<Table>(myDb.Tables, options, scriptBuilder);
            script<StoredProcedure>(myDb.StoredProcedures, options, scriptBuilder);
            script<View>(myDb.Views, options, scriptBuilder);
            script<User>(myDb.Users, options, scriptBuilder);
            script<UserDefinedFunction>(myDb.UserDefinedFunctions, options, scriptBuilder);

            return scriptBuilder.ToString();
        }

        static void script<T>(IEnumerable enumerable, ScriptingOptions options, StringBuilder scriptBuilder) where T : IScriptable
        {
            foreach (T t in enumerable)
            {
                script<T>(t, options, scriptBuilder);
            }
        }

        static void script<T>(T t, ScriptingOptions options, StringBuilder scriptBuilder) where T : IScriptable
        {
            foreach (string script in t.Script(options))
            {
                //Create
                scriptBuilder.AppendLine(script);
            }
        }

        static void script<T>(T t, ScriptingOptions options, StringCollection scripts) where T : IScriptable
        {
            foreach (string script in t.Script(options))
            {
                scripts.Add(script);
            }
        }
    }
}
