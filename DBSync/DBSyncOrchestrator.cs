using Microsoft.Synchronization;
using Microsoft.Synchronization.Data.SqlServer;
using System;
using Microsoft.Synchronization.Data;
using System.Configuration;
using FinLib;
using FinLib.Logger;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Collections.Specialized;
using static DBSync.ScriptDB;

namespace DBSync
{
    class DBSyncOrchestrator : SyncOrchestrator, IEnumerator<DBSyncOrchestrator>
    {
        readonly String scope;
        public readonly DBConnection source;
        public readonly DBConnection destination;
        readonly Stack<string> databases = new Stack<string>();
        //private IEnumerator<string> databases;
        string myDatabase = null;

        public DBSyncOrchestrator(String scope, String source, String destination) : this(scope, ConfigurationManager.ConnectionStrings["source"], ConfigurationManager.ConnectionStrings["destination"]) { }
        public DBSyncOrchestrator(String scope, ConnectionStringSettings source, ConnectionStringSettings destination) : this(scope, new DBConnection(source.ConnectionString), new DBConnection(destination.ConnectionString)) { }
        public DBSyncOrchestrator(String scope, DBConnection source, DBConnection destination)
        {
            this.scope = scope;
            this.Direction = SyncDirectionOrder.Download;
            this.source = source;
            this.destination = destination;
            // Stack<string> databases = new Stack<string>(source.databaseNames.GetEnumerator());
            foreach (string db in source.databaseNames)
            {
                databases.Push(db);
            }
        }

        public String database
        {
            get
            {
                return myDatabase;
            }
        }

        public DBSyncOrchestrator Current
        {
            get
            {
                if (databases == null)
                {
                    return null;
                }
                return this;
            }
        }

        object IEnumerator.Current
        {
            get
            {
                return this.Current;
            }
        }

        public void Backup()
        {
            if (Settings.Default.BackupSource)
            {
                try
                {
                    foreach (String database in source.databaseNames)
                    {
                        source.backup(database);
                    }
                }
                catch (Exception e)
                {
                    Log.f(e);
                }
            }
            if (Settings.Default.BackupDestination)
            {
                try
                {
                    foreach (String database in destination.databaseNames)
                    {
                        destination.backup(database);
                    }
                }
                catch (Exception e)
                {
                    Log.f(e);
                }
            }
        }

        public void Dispose()
        {
            source.connection.Close();
            destination.connection.Close();
            databases.Clear();
            myDatabase = null;
        }

        public bool hasNext
        {
            get
            {
                return databases != null && databases.Count > 0;
            }
        }

        public bool MoveNext()
        {

            if (databases != null && databases.Count > 0)
            {
                myDatabase = databases.Pop();
                source.database = database;

                if (!destination.databaseNames.Contains(database))
                {
                    //destinataion database doesent exist, create it
                    if (!destination.createDatabase(database))
                    {
                        Reports.add("Fatal", "Failed to create Database: ", database, " for: ", destination.server.Name);
                    }
                }

                destination.database = database;

                return true;
            }
            return false;
        }

        public void Reset()
        {
            myDatabase = null;
            databases.Clear();
            foreach (string db in source.databaseNames)
            {
                databases.Push(db);
            }
        }

        public void execute()
        {
            if (Settings.Default.Debug)
            {
                foreach (string db in source.databaseNames)
                {
                    Log.v("Source Datase:", db);
                }
                foreach (string db in destination.databaseNames)
                {
                    Log.v("Destination Datase:", db);
                }
            }
            if (Settings.Default.BackupSource || Settings.Default.BackupDestination)
            {
                Backup();
            }
            if (Settings.Default.SyncUsers)
            {
                SyncUsers();
            }
            if (Settings.Default.SyncData)
            {
                SyncData();
            }

            if (Settings.Default.SyncProcedures)
            {
                SyncProcedures();
            }

            if (Settings.Default.SyncFunctions)
            {
                SyncFunctions();
            }

            if (Settings.Default.SyncViews)
            {
                SyncViews();
            }
        }

        void SyncUsers()
        {
            Dictionary<string, Model.Login> sourceLogins = source.logins;
            Dictionary<string, Model.Login> destinationLogins = destination.logins;

            if (Settings.Default.DropMissingUsers)
            {
                //drop non existant logins
                foreach (KeyValuePair<string, Model.Login> login in destinationLogins)
                {
                    if (!sourceLogins.ContainsKey(login.Key))
                    {
                        Log.v("Destination Login Doesn't Exist On Source, Dropping:", login.Key, " ID:", login.Value.sid, " Hash:", login.Value.passwordHash);
                        login.Value.drop(destination.connection);
                    }
                }
            }

            //create missing logins
            foreach (KeyValuePair<string, Model.Login> login in sourceLogins)
            {
                if (!destinationLogins.ContainsKey(login.Key) || !Equality.equals<Model.Login>(login.Value, destinationLogins[login.Key]))
                {
                    Log.v("Source Login Doesn't Exist On Destination, Creating:", login.Key, " ID:", login.Value.sid, " Hash:", login.Value.passwordHash);
                    login.Value.create(destination.connection);
                }
            }
        }

        void SyncFunctions()
        {
            Log.i("Syncing Functions");
            if (database != null)
            {
                Log.v("Resseting Database Enumeration");
                Reset();
            }

            while (hasNext)
            {
                try
                {
                    MoveNext();
                    Log.i("Starting Functions Sync For:", database);

                    List<String> sourceFunctions = source.functionNames;
                    if (sourceFunctions.Count > 0)
                    {
                        List<String> destinationFunctions = destination.functionNames;
                        foreach (string function in sourceFunctions)
                        {
                            Log.i("Checking for function:", function, " in:", database);
                            string sourceFunctionScript = source.functionScript(function);
                            if (destinationFunctions.Contains(function))
                            {
                                Log.v("Destination View Exists");
                                string destinationFunctionScript = destination.functionScript(function);
                                if (Equality.Equals(sourceFunctionScript, destinationFunctionScript))
                                {
                                    Log.v("View already Sync'd");
                                    continue;
                                }
                            }
                            destination.execSql(sourceFunctionScript);
                            Log.v("View Sycn'd");
                        }
                    }
                    else
                    {
                        Log.v("No Functions To Sync", database);
                    }

                }
                catch (Exception e)
                {
                    Reports.add("FATAL", "Failed to Sync Database:", database, " with exception:", e.Message, " ", e.objectToString());
                    Log.f(e);
                }
                Log.i("Function Sync Complete");
            }
        }

        void SyncProcedures()
        {
            Log.i("Syncing Procedures");
            if (database != null)
            {
                Log.v("Resseting Database Enumeration");
                Reset();
            }

            while (hasNext)
            {
                try
                {
                    MoveNext();
                    Log.i("Starting Procedure Sync For:", database);

                    List<String> sourceProcedures = source.procedureNames;
                    if (sourceProcedures.Count > 0)
                    {
                        List<String> destinationProcedures = destination.procedureNames;
                        foreach (string procedure in sourceProcedures)
                        {
                            Log.i("Checking for procedure:", procedure, " in:", database);
                            StringCollection sourceProcedureScript = source.procedureScript(procedure);
                            if (destinationProcedures.Contains(procedure))
                            {
                                Log.v("Destination Procedure Exists");
                                StringCollection destinationProcedureScript = destination.procedureScript(procedure);
                                if (Equality.Equals(sourceProcedureScript, destinationProcedureScript))
                                {
                                    Log.v("Procedure already Sync'd");
                                    continue;
                                }
                            }
                            destination.execSql(sourceProcedureScript);
                            Log.v("Procedure Sync'd");
                        }
                    }
                    else
                    {
                        Log.v("No Procedures To Sync", database);
                    }
                }
                catch (Exception e)
                {
                    Reports.add("FATAL", "Failed to Sync Database:", database, " with exception:", e.Message, " ", e.objectToString());
                    Log.f(e);
                }

            }
            Log.i("Procedure Sync Complete");
        }
        void SyncViews()
        {
            Log.i("Syncing Views");
            if (database != null)
            {
                Log.v("Resseting Database Enumeration");
                Reset();
            }

            while (hasNext)
            {
                try
                {
                    MoveNext();
                    Log.i("Starting View Sync For:", database);

                    List<String> sourceViews = source.viewNames;
                    if (sourceViews.Count > 0)
                    {
                        List<String> destinationViews = destination.viewNames;
                        foreach (String view in sourceViews)
                        {
                            Log.i("Checking for view:", view, " in:", database);
                            string sourceViewScript = source.viewScript(view);
                            if (destinationViews.Contains(view))
                            {
                                Log.v("Destination View Exists");
                                string destinationViewScript = destination.viewScript(view);
                                if (Equality.Equals(sourceViewScript, destinationViewScript))
                                {
                                    Log.v("View already Sync'd");
                                    continue;
                                }
                            }
                            destination.execSql(sourceViewScript);
                            Log.v("View Sync'd");
                        }
                    }
                    else
                    {
                        Log.v("No Views To Sync", database);
                    }
                }
                catch (Exception e)
                {
                    Reports.add("FATAL", "Failed to Sync Database:", database, " with exception:", e.Message, " ", e.objectToString());
                    Log.f(e);
                }
            }
            Log.i("View Sync Complete");
        }

        void SyncData()
        {
            if (database != null)
            {
                Reset();
            }
            while (hasNext)
            {
                try
                {
                    MoveNext();
                    provision();
                    SyncOperationStatistics syncStats = base.Synchronize();
                    Reports.add("Stats", database, ": ", syncStats.objectToString());
                }
                catch (Exception e)
                {
                    Reports.add("FATAL", "Failed to Sync Database:", database, " with exception:", e.Message, " ", e.objectToString());
                    Log.f(e);
                }
            }
        }


        new public SyncOperationStatistics Synchronize()
        {
            throw new UnauthorizedAccessException();
        }

        //void deprovision()
        //{
        //    try {
        //        source.deprovisionScope(scope, "");
        //    }catch(Exception e)
        //    {
        //        Log.f(e);
        //    }
        //    try { 
        //        destination.deprovisionScope(scope, "");
        //    }
        //    catch (Exception e)
        //    {
        //        Log.f(e);
        //    }
        //}

        void provision()
        {
            Log.v("Database:", database);


            SqlSyncScopeProvisioning sourceProvisioning = new SqlSyncScopeProvisioning(source.connection);
            sourceProvisioning.CommandTimeout = Settings.Default.CommandTimeout;
            sourceProvisioning.ObjectPrefix = Settings.Default.ObjectPrefix;

            SqlSyncScopeProvisioning destionationProvisioning = new SqlSyncScopeProvisioning(destination.connection);
            destionationProvisioning.CommandTimeout = Settings.Default.CommandTimeout;
            destionationProvisioning.ObjectPrefix = Settings.Default.ObjectPrefix;

            DbSyncScopeDescription liveScope = source.buildScopeDescription(scope);

            // no tables to sync, move along
            if (liveScope.Tables.Count == 0)
            {
                return;
            }

            //If the scope exists, test to see if reprovisioning is necessary
            if (sourceProvisioning.ScopeExists(scope))
            {
                Log.i("Source Scope ", scope, " Exists, testing for reprovision");
                DbSyncScopeDescription definedScope = source.getScopeDescription(scope);

                if (!Equality.equals(liveScope, definedScope))
                {
                    Log.i("Source Scope ", scope, " has changed");
                    //mark scope for recreation by deleteing it
                    source.deprovisionScope(scope);
                }
                else
                {
                    Log.i("Source Scope ", scope, " provision up to date");
                }
            }

            //if the provisioning scope doesn't exist, provision tables
            if (!sourceProvisioning.ScopeExists(scope))
            {
                Log.i("Provisioning Source Database For Scope:", scope);

                sourceProvisioning.PopulateFromScopeDescription(liveScope);
                sourceProvisioning.SetCreateTableDefault(DbSyncCreationOption.CreateOrUseExisting);

                Log.v(sourceProvisioning.ObjectPrefix);
                
                sourceProvisioning.Apply();

                //if the destination provisioning exists
                if (destionationProvisioning.ScopeExists(scope))
                {
                    Log.i("Marking Destination Scope for Reprovisioning");
                    destination.deprovisionScope(scope);
                }
            }

            this.RemoteProvider = source.getSyncProvider(scope);
            Log.i("Source provisioning complete. Source Provider Registered");

            
            if (destionationProvisioning.ScopeExists(scope))
            {
                DbSyncScopeDescription definedScope = destination.getScopeDescription(scope);
                if (!Equality.equals(definedScope, liveScope))
                {
                    Log.v("Destination Provisioning Expired");
                    destination.deprovisionScope(scope);
                }
            }

            if (!destionationProvisioning.ScopeExists(scope))
            {
                List<string> scripts = findTableDescriptionDifferences(source.tableScripts(), destination.tableScripts());

                foreach (string script in scripts)
                {
                    destination.execSql(script);
                }

                Log.i("Provisioning Destination Database For Scope:" + scope);
                destionationProvisioning.PopulateFromScopeDescription(liveScope);
                destionationProvisioning.SetCreateTableDefault(DbSyncCreationOption.CreateOrUseExisting); //CreateOrUseExisting
                
                destionationProvisioning.Apply();
            }

            this.LocalProvider = destination.getSyncProvider(scope);
            Log.i("Destination provisioning complete. Destinataion Provider Registered");
        }


        List<string> findTableDescriptionDifferences(Dictionary<string, TableScript> source, Dictionary<string, TableScript> destination)
        {
            List<string> differences = new List<string>();

            HashSet<string> keys = new HashSet<string>();
            keys.UnionWith(source.Keys);
            keys.UnionWith(destination.Keys);

            foreach (string key in keys)
            {
                try
                {
                   TableScript sTable = source.ContainsKey(key)? source[key]:null;
                   TableScript dTable = destination.ContainsKey(key) ? destination[key] : null;

                    if (sTable == null)
                    {
                        differences.Add(dTable.drop);
                    } else if (dTable == null) {
                        differences.Add(sTable.create);
                    } else if(sTable.create != dTable.create)
                    {
                        differences.Add(dTable.drop);
                        differences.Add(sTable.create);
                    }
                }
                catch (Exception e)
                {
                    Log.f(e);
                }
            }

            return differences;
        }
    }
}

