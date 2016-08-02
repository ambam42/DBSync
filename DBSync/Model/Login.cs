using FinLib.Logger;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBSync.Model
{
    class Login
    {
        const String NAME = "Name";
        const String PASSWORD_HASH = "PasswordHashString";
        const String SID = "SIDSTring";

        public readonly string name;
        public readonly string passwordHash;
        public readonly string sid;
        public readonly List<Role> roles;
        public readonly List<Permission> permissions;

        public Login(object name, object passwordHash, object sid, Dictionary<string, List<Role>> roles, Dictionary<string, List<Permission>> permissions) : this(name.objectToString(), passwordHash.objectToString(), sid.objectToString(), roles, permissions){ }
        public Login(string name, string passwordHash, string sid, Dictionary<string, List<Role>> roles, Dictionary<string, List<Permission>> permissions)
        {
            this.name = name;
            this.passwordHash = passwordHash;
            this.sid = sid;
            this.roles = roles.ContainsKey(this.name) ? roles[this.name] : new List<Role>();
            this.permissions = permissions.ContainsKey(this.name) ? permissions[this.name] : new List<Permission>();
        }

        public bool drop(SqlConnection connection)
        {
            try {
                new SqlCommand(dropStatement, connection).ExecuteNonQuery();
                return true;
            }catch(Exception e)
            {
                Log.f(e);
                Reports.add("Fatal", "Error droping user:", name," From:", connection.DataSource, e.objectToString());
            }
            return false;
        }
        String dropStatement
        {
            get
            {
                return new StringBuilder("IF EXISTS (SELECT * FROM sys.syslogins WHERE name = N'").Append(name).Append("') DROP LOGIN ").Append(name).Append(";").ToString();
            }
        }

        public bool create(SqlConnection connection)
        {
            drop(connection);
            try
            { 
                new SqlCommand(createStatement, connection).ExecuteNonQuery();

                foreach (Model.Role r in roles)
                {
                    r.create(connection);
                }

                foreach (Model.Permission p in permissions)
                {
                    p.create(connection);
                }
                return true;
            }
            catch (Exception e)
            {
                Log.f(e);
                Reports.add("Fatal", "Error creating user:", name, " From:", connection.DataSource, e.objectToString());
            }
            return false;
        }

        String createStatement
        {
            get
            {
                return new StringBuilder("Create Login ")
                    .Append(name)
                    .Append(" With Password = ")
                    .Append(passwordHash)
                    .Append(" HASHED, SID=")
                    .Append(sid)
                    .Append(";")
                    .ToString();
            }
        }

        public static Login from(Dictionary<string, List<Role>> roles, Dictionary<string, List<Permission>> permissions, SqlDataReader reader)
        {
            return new Login(reader[NAME], reader[PASSWORD_HASH].objectToString(), reader[SID].objectToString(), roles, permissions);
        }

        public static Dictionary<string, Login> from(SqlConnection connection)
        {
            Dictionary<string, Login> logins = new Dictionary<string, Login>();
            string db = null;
            try {
                if (connection.Database != "master")
                {
                    db = connection.Database;
                    connection.ChangeDatabase("master");
                }

                Dictionary<string, List<Role>> roles = Role.from(connection);
                Dictionary<string, List<Permission>> permissions = Permission.from(connection);

                new SqlCommand(DROP_SQL, connection).ExecuteNonQuery();
                new SqlCommand(CREATE_SQL, connection).ExecuteNonQuery();

                SqlCommand com = new SqlCommand(EXECUTE_SQL, connection);
                com.CommandType = System.Data.CommandType.StoredProcedure;

                using (SqlDataReader reader = com.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Login login = from(roles, permissions, reader);
                        logins.Add(login.name, login);
                    }
                }

                if (db != null)
                {

                }
            }catch(Exception e)
            {
                Log.f(e);
            }
            finally
            {
                if (db != null)
                {
                    try
                    {
                        connection.ChangeDatabase(db);
                    }catch(Exception e)
                    {
                        Log.f(e);
                    }
                }
            }
            return logins;
        }

        const string DROP_SQL = @"
        If Exists (Select 1 From INFORMATION_SCHEMA.ROUTINES
			        Where ROUTINE_NAME = 'dba_GetLogins'
			        And ROUTINE_SCHEMA = 'dbo')
	        Drop Procedure dbo.dba_GetLogins
        ";

        const string CREATE_SQL = @"
        Create Procedure dbo.dba_GetLogins
        As

        Declare @MaxID int,
		        @CurrID int,	
		        @SQL nvarchar(max),
		        @LoginName sysname,
		        @SID varbinary(85),
		        @SIDString nvarchar(100),
		        @PasswordHash varbinary(256),
		        @PasswordHashString nvarchar(300)

        Declare @Logins Table (LoginID int identity(1, 1) not null primary key,
						        [Name] sysname not null,
						        [SID] varbinary(85) not null,
						        [SIDString] nvarchar(100) null,
						        IsDisabled int not null,
						        [Type] char(1) not null,
						        PasswordHash varbinary(256) null,
						        PasswordHashString nvarchar(300) null)

        set @SQL = 'select P.name, P.sid, P.is_disabled, P.type, L.password_hash
	        From master.sys.server_principals P
	        Left Join master.sys.sql_logins L On L.principal_id = P.principal_id
        WHERE (P.type IN(''S'') AND P.name NOT IN (''sa'', ''guest'') AND P.name not like (''##%''));';

        Insert Into @Logins (Name, SID, IsDisabled, Type, PasswordHash)
        Exec sp_executesql @SQL;

        Select @MaxID = Max(LoginID), @CurrID = 1
        From @Logins;

        While @CurrID <= @MaxID
	        Begin
		        SELECT @LoginName = Name,
			        @SID = [SID],
			        @PasswordHash = PasswordHash
		        FROM @Logins
		        WHERE LoginID = @CurrID;

		        Set @SIDString = '0x' +Cast('' As XML).value('xs:hexBinary(sql:variable(""@SID""))', 'nvarchar(100)');
		        Set @PasswordHashString = '0x' + Cast('' As XML).value('xs:hexBinary(sql:variable(""@PasswordHash""))', 'nvarchar(300)');

                UPDATE @Logins

                SET
                    SIDString = @SIDString,
                    PasswordHashString = @PasswordHashString

                WHERE LoginID = @CurrID;

                Set @CurrID = @CurrID + 1;
                End

        SELECT* FROM @Logins
        RETURN
        ";

        const string EXECUTE_SQL = "dbo.dba_GetLogins";

        //const string LOGINS_SQL =
        //@"
        //Use master;
        //Go

        //If Exists (Select 1 From INFORMATION_SCHEMA.ROUTINES
			     //   Where ROUTINE_NAME = 'dba_GetLogins'
			     //   And ROUTINE_SCHEMA = 'dbo')
	       // Drop Procedure dbo.dba_GetLogins
        //    Go

        //SET ANSI_NULLS ON
        //SET QUOTED_IDENTIFIER ON
        //GO

        //Create Procedure dbo.dba_GetLogins
        //As

        //Declare @MaxID int,
		      //  @CurrID int,	
		      //  @SQL nvarchar(max),
		      //  @LoginName sysname,
		      //  @SID varbinary(85),
		      //  @SIDString nvarchar(100),
		      //  @PasswordHash varbinary(256),
		      //  @PasswordHashString nvarchar(300)

        //Declare @Logins Table (LoginID int identity(1, 1) not null primary key,
						  //      [Name] sysname not null,
						  //      [SID] varbinary(85) not null,
						  //      [SIDString] nvarchar(100) null,
						  //      IsDisabled int not null,
						  //      [Type] char(1) not null,
						  //      PasswordHash varbinary(256) null,
						  //      PasswordHashString nvarchar(300) null)

        //set @SQL = 'select P.name, P.sid, P.is_disabled, P.type, L.password_hash
	       // From master.sys.server_principals P
	       // Left Join master.sys.sql_logins L On L.principal_id = P.principal_id
        //WHERE (P.type IN(''S'') AND P.name NOT IN (''sa'', ''guest'') AND P.name not like (''##%''));';

        //Insert Into @Logins (Name, SID, IsDisabled, Type, PasswordHash)
        //Exec sp_executesql @SQL;

        //Select @MaxID = Max(LoginID), @CurrID = 1
        //From @Logins;

        //While @CurrID <= @MaxID
	       // Begin
		      //  SELECT @LoginName = Name,
			     //   @SID = [SID],
			     //   @PasswordHash = PasswordHash
		      //  FROM @Logins
		      //  WHERE LoginID = @CurrID;

		      //  Set @SIDString = '0x' +Cast('' As XML).value('xs:hexBinary(sql:variable(""@SID""))', 'nvarchar(100)');
		      //  Set @PasswordHashString = '0x' + Cast('' As XML).value('xs:hexBinary(sql:variable(""@PasswordHash""))', 'nvarchar(300)');

        //        UPDATE @Logins

        //        SET
        //            SIDString = @SIDString,
        //            PasswordHashString = @PasswordHashString

        //        WHERE LoginID = @CurrID;

        //        Set @CurrID = @CurrID + 1;
        //        End

        //SELECT* FROM @Logins
        //RETURN
        //GO

        //EXEC dbo.dba_GetLogins
        //";
    }
}
