using FinLib.Logger;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.Synchronization.Data;
using Microsoft.Synchronization.Data.SqlServer;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.SqlClient;
using System.Text;
using static DBSync.ScriptDB;

namespace DBSync
{
    class DBConnection {
        const string INITIAL_CATALOG = "Initial Catalog";
        Dictionary<string, string> connectionDetails;
        SqlConnection myConnection;

        public DBConnection(string connectionString)
        {
            connectionDetails = connectionString.toDictionary(';', '=');
            myConnection = new SqlConnection(connectionDetails.Collapse<KeyValuePair<string, string>>(x => new StringBuilder(x.Key).Append('=').Append(x.Value).ToString(), ";"));
            myConnection.Open();
        }

        public SqlConnection connection
        {
            get
            {
                return myConnection;
            }
        }

        public string database
        {
            get
            {
                return myConnection.Database;
            }
            set
            {
                myConnection.Close();

                connectionDetails[INITIAL_CATALOG] = value;
                myConnection.Dispose();

                myConnection = new SqlConnection(connectionDetails.Collapse<KeyValuePair<string, string>>(x => new StringBuilder(x.Key).Append('=').Append(x.Value).ToString(), ";"));
                myConnection.Open();
            }
        }

        public List<string> databaseNames
        {
            get
            {
                List<string> databaseList = new List<string>();
                StringBuilder sqlCommand = new StringBuilder("SELECT name FROM sys.databases");

                StringBuilder whereBuilder = new StringBuilder();
                if (Settings.Default.DBBlackList != null && Settings.Default.DBBlackList.Count > 0)
                {
                    Settings.Default.DBBlackList.Collapse(" AND ", "(name NOT LIKE '", "')", whereBuilder);
                }

                if (Settings.Default.DBWhiteList != null && Settings.Default.DBWhiteList.Count > 0)
                {
                    if (whereBuilder.Length > 0) { whereBuilder.Prepend("(").Append(")"); }
                    Settings.Default.DBWhiteList.Collapse(null, " OR (name LIKE '", "')", whereBuilder);
                }

                if (whereBuilder.Length > 0) { sqlCommand.Append(" Where(").Append(whereBuilder.ToString()).Append(")"); }

                using (SqlCommand com = new SqlCommand(sqlCommand.ToString(), connection))
                {
                    com.CommandTimeout = Settings.Default.CommandTimeout;
                    using (SqlDataReader reader = com.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            databaseList.Add((string)reader["name"]);
                        }
                    }
                }
                return databaseList;
            }
        }

        public List<string> procedureNames
        {
            get
            {
                List<string> procedureList = new List<string>();
                StringBuilder sqlCommand = new StringBuilder("SELECT o.name FROM sys.objects o LEFT OUTER JOIN sys.extended_properties ep ON o.object_id = ep.major_id");

                StringBuilder whereBuilder = new StringBuilder("o.name IS NOT NULL")
                    .Append(" AND (o.is_ms_shipped is null or o.is_ms_shipped = 0)")
                    .Append(" AND (ep.name is null or ep.name not like 'microsoft_database_tools_support')")
                    .Append(" AND o.type = 'P'")
                    .Append(" AND o.name not like '%_selectchanges'");

                if (Settings.Default.ObjectPrefix.Length > 0)
                {
                    whereBuilder.Append(" AND O.name not like '").Append(Settings.Default.ObjectPrefix).Append("%'");
                }

                sqlCommand.Append(" Where(").Append(whereBuilder.ToString()).Append(")");

                using (SqlCommand com = new SqlCommand(sqlCommand.ToString(), connection))
                {
                    com.CommandTimeout = Settings.Default.CommandTimeout;
                    using (SqlDataReader reader = com.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            procedureList.Add((string)reader["name"]);
                        }
                    }
                }

                return procedureList;
            }
        }

        public List<string> functionNames
        {
            get
            {
                List<string> functionList = new List<string>();
                StringBuilder sqlCommand = new StringBuilder("SELECT o.name FROM sys.objects o LEFT OUTER JOIN sys.extended_properties ep ON o.object_id = ep.major_id");

                StringBuilder whereBuilder = new StringBuilder("o.name IS NOT NULL")
                    .Append(" AND ISNULL(o.is_ms_shipped, 0) = 0")
                    .Append(" AND (ep.name is null or ep.name not like 'microsoft_database_tools_support')")
                    .Append(" AND o.type in ('FN', 'AF', 'FS', 'FT', 'IF', 'TF')");

                sqlCommand.Append(" Where(").Append(whereBuilder.ToString()).Append(")");

                using (SqlCommand com = new SqlCommand(sqlCommand.ToString(), connection))
                {
                    com.CommandTimeout = Settings.Default.CommandTimeout;
                    using (SqlDataReader reader = com.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            functionList.Add((string)reader["name"]);
                        }
                    }
                }

                return functionList;
            }
        }
        public List<string> viewNames
        {
            get
            {
                List<string> viewList = new List<string>();
                StringBuilder sqlCommand = new StringBuilder("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES");

                StringBuilder whereBuilder = new StringBuilder("TABLE_TYPE like '%VIEW%'");

                sqlCommand.Append(" Where(").Append(whereBuilder.ToString()).Append(")");

                using (SqlCommand com = new SqlCommand(sqlCommand.ToString(), connection))
                {
                    com.CommandTimeout = Settings.Default.CommandTimeout;
                    using (SqlDataReader reader = com.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            viewList.Add((string)reader["TABLE_NAME"]);
                        }
                    }
                }

                return viewList;
            }
        }

        public List<string> tableNames
        {
            get
            {
                List<string> tableList = new List<string>();
                StringBuilder sqlCommand = new StringBuilder("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES");

                StringBuilder whereBuilder = new StringBuilder("TABLE_TYPE like '%TABLE%'");

                if (Settings.Default.ObjectPrefix.Length > 0)
                {
                    whereBuilder.Append(" AND TABLE_NAME NOT LIKE '").Append(Settings.Default.ObjectPrefix).Append("_%'");
                }
                if (Settings.Default.TableBlackList != null && Settings.Default.TableBlackList.Count > 0)
                {
                    //whereBuilder.Append(" AND ");
                    Settings.Default.TableBlackList.Collapse(null, " AND (TABLE_NAME NOT LIKE '", "')", whereBuilder);
                }

                bool schemaBlackList = Settings.Default.SchemaBlackList != null && Settings.Default.SchemaBlackList.Count > 0;
                bool schemaWhiteList = Settings.Default.SchemaWhiteList != null && Settings.Default.SchemaWhiteList.Count > 0;
                if (schemaBlackList || schemaWhiteList)
                {
                    whereBuilder.Append(" AND (");

                    if (schemaBlackList)
                    {
                        if(schemaWhiteList) { whereBuilder.Append("("); }
                        Settings.Default.SchemaBlackList.Collapse(" AND ", "(TABLE_SCHEMA NOT LIKE '", "')", whereBuilder);
                        if (schemaWhiteList) { whereBuilder.Append(") OR ("); }
                    }
                    if (schemaWhiteList)
                    {
                        Settings.Default.SchemaWhiteList.Collapse(" OR ", "(TABLE_SCHEMA LIKE '", "')", whereBuilder);
                        if (schemaBlackList) { whereBuilder.Append(")"); }
                    }
                    whereBuilder.Append(")");
                }
                sqlCommand.Append(" Where(").Append(whereBuilder.ToString()).Append(")");

                using (SqlCommand com = new SqlCommand(sqlCommand.ToString(), connection))
                {
                    com.CommandTimeout = Settings.Default.CommandTimeout;
                    using (SqlDataReader reader = com.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tableList.Add((string)reader["TABLE_NAME"]);
                        }
                    }
                }

                return tableList;
            }
        }

        public Dictionary<string, Model.Login> logins
        {
            get
            {
                return Model.Login.from(connection);
            }
        }

        //public bool createLogin(Model.Login login)
        //{

        //}

        public bool createDatabase(string databaseName)
        {
            return execSql(new StringBuilder("Create Database ").Append(databaseName).ToString());
        }

        public bool createUser(Model.Login login)
        {
            return login.create(connection);
        }

        public bool createSchema(string schema)
        {
            return execSql(new StringBuilder("IF NOT EXISTS (SELECT schema_name FROM information_schema.schemata WHERE schema_name = '")
                .Append(schema).Append("') EXEC sp_executesql N'CREATE SCHEMA ").Append(schema).Append("'").ToString());
        }

        public bool backup(string db)
        {
            return execSql(new StringBuilder("BACKUP DATABASE [")
                .Append(db)
                .Append("] TO DISK = N'C:\\Backups\\")
                .Append(db)
                .Append("_db.bak' WITH NOFORMAT, NOINIT, NAME = N'")
                .Append(db)
                .Append(" Full Database Backup', SKIP, NOREWIND, NOUNLOAD, STATS = 10").ToString());
        }

        public bool execSql(StringCollection sqlCommands)
        {
            try
            {
                Log.v("Executing batch of scripts");
                foreach(string sqlCommand in sqlCommands)
                {
                    if (!execSql(sqlCommand))
                    {
                        Log.f("Error Processing Batch, Stopping.");
                        return false;
                    }
                }
                return true;
            }catch(Exception e)
            {
                Log.f(e);
            }
            return false;
        }

        public bool execSql(string sqlCommand)
        {
            try
            {
                SqlCommand cmd = new SqlCommand(sqlCommand, connection);
                cmd.CommandTimeout = Settings.Default.CommandTimeout;
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception e)
            {
                Log.f("Error Executing Sql:", sqlCommand);
                Log.f(e);
            }
            return false;
        }

        //build a sync scope description using all the tables that are not in the blacklist
        public DbSyncScopeDescription buildScopeDescription(string scope)
        {            
            DbSyncScopeDescription sourceScope = new DbSyncScopeDescription(scope);
            
            //Create a scope comment which will help us detect when reprovisioning is necessary  
            StringBuilder scopeComment = new StringBuilder(scope);
            
            List<string> tables = tableNames;
            scopeComment.Append(":").Append(tables.Count);

            //foreach each table that meet's are criteria, check to see if it can be sync'd or a notificaiton needs to be sent
            foreach (string table in tables)
            {
                DbSyncTableDescription syncTableDescription = getTableDescription(table);
                bool hasPrimaryKey = false;

                DbSyncColumnDescription primarySub = null;
                foreach (DbSyncColumnDescription syncColumnDescription in syncTableDescription.Columns)
                {
                    //work around for microsoft bug when building scopes vs inflating scopes
                    if (syncColumnDescription.Type == "money")
                    {
                        syncColumnDescription.ScaleSpecified = true;
                        syncColumnDescription.PrecisionSpecified = true;
                    }
                    
                    //if we haven't detected a primary key yet, check to see if this column will work
                    if (!hasPrimaryKey || primarySub == null)
                    {
                        if (syncColumnDescription.IsPrimaryKey)
                        {
                            //fix for another microsoft bug, tables with timestamp set as the primary key can't be synced
                            if(syncColumnDescription.Type != "timestamp")
                            {
                                //we found a primary key and it's a valid data type
                                hasPrimaryKey = true;
                            }
                            else
                            {
                                //we found an invalid primary key, generate a warning report
                                syncColumnDescription.IsPrimaryKey = false;
                                Reports.add("Alert", "WARNING - INVALID PRIMARY KEY TYPE, TIMESTAMP - Database:", database, " Table:", syncTableDescription.GlobalName, " Column:", syncColumnDescription.QuotedName);
                            }
                            
                        }
                        else if (syncColumnDescription.AutoIncrementStepSpecified)
                        {
                            //if this column uses autoincrement, then we can substitute it for a primary if we can't find another one
                            primarySub = syncColumnDescription;
                        }
                    }
                }

                if (!hasPrimaryKey && primarySub!=null)
                {
                    //if there's no primary key, but we found a substitute, set it as the primary key, but generate a warning
                    primarySub.IsPrimaryKey = hasPrimaryKey = true;
                    Reports.add("Alert", "WARNING - TABLE MISSING PRIMARY KEY, AUTOINCREMENT COLUMN SUBSTITUED - Database:", database, " Table:", syncTableDescription.GlobalName, " AutoIncrement Column:", primarySub.QuotedName);
                }

                if (hasPrimaryKey)
                {
                    //there's a primary key, this table can be sync'd, add it to the scope description
                    sourceScope.Tables.Add(syncTableDescription);
                    scopeComment.Append(":").Append(table);
                }
                else {
                    //table is unsyncable, generate a Fatal warning so a primary key will be added
                    //GENERATE A FATAL WARNING
                    Reports.add("Alert", "FATAL - TABLE MISSING PRIMARY KEY, UNSYNCABLE - Database:", database, " Table:", syncTableDescription.GlobalName);
                }
            }
            
            sourceScope.UserComment = scopeComment.ToString();

            return sourceScope;
        }

        public string script
        {
            get
            {
                return ScriptDB.ScriptDatabase(server, database);
            }
        }

        //public string viewScript
        //{
        //    get
        //    {
        //        return ScriptDB.ScriptViews(server, database);
        //    }
        //}

        public bool dropView(string view)
        {
            try
            {
                return execSql(new StringBuilder("DROP VIEW IF EXISTS [").Append(view).Append("];").ToString());
            }
            catch(Exception e)
            {
                Log.f(e);
            }
            return false;
        }

        public Dictionary<string, TableScript> tableScripts()
        {
            return ScriptDB.ScriptTables(server, database, tableNames);
        }
        public string viewsScript(List<string> views)
        {
            return ScriptDB.ScriptViews(server, database, views);
        }

        public string viewScript(string view)
        {
            return ScriptDB.ScriptView(server, database, view);
        }

        public StringCollection procedureScript(string procedure)
        {
            return ScriptDB.ScriptProcedure(server, database, procedure);
        }

        public string functionScript(string function)
        {
            return ScriptDB.ScriptFunction(server, database, function);
        }

        public Server server
        {
            get
            {
                return new Server(new ServerConnection(connection));
            }
        }

        public DbSyncScopeDescription getScopeDescription(string scope)
        {
         
            return SqlSyncDescriptionBuilder.GetDescriptionForScope(scope, Settings.Default.ObjectPrefix, connection);
        }

        public DbSyncTableDescription getTableDescription(string table)
        {
            return SqlSyncDescriptionBuilder.GetDescriptionForTable(table, connection);
        }

        public SqlSyncTableProvisioning getTableProvisioning(string table)
        {
            return new SqlSyncTableProvisioning(connection, getTableDescription(table));
        }

        public SqlSyncTableProvisioning getTableProvisioning(DbSyncTableDescription table)
        {
            return new SqlSyncTableProvisioning(connection, table);
        }

        public SqlSyncProvider getSyncProvider(string scope)
        {
            SqlSyncProvider ssp = new SqlSyncProvider(scope, connection, Settings.Default.ObjectPrefix);
            ssp.CommandTimeout = Settings.Default.CommandTimeout;
            return ssp;
        }

        public void deprovisionScope(string scope, string ObjectPrefix=null)
        {
            Log.i("Server:",connection.DataSource, " Database:", connection.Database,  " Deprovisioning Scope:", scope);

            SqlSyncScopeDeprovisioning deprovisioning = new SqlSyncScopeDeprovisioning(connection);
            deprovisioning.ObjectPrefix = ObjectPrefix!=null?ObjectPrefix : Settings.Default.ObjectPrefix;
            deprovisioning.DeprovisionScope(scope);
        }
    }
}
