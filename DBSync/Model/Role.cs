using FinLib.Logger;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBSync.Model
{
  //  Select RoleP.name, LoginP.name' + CHAR(10) +
		//'From ' + QUOTENAME(@PartnerServer) + '.master.sys.server_role_members RM' + CHAR(10) +
		//'Inner Join ' + QUOTENAME(@PartnerServer) + '.master.sys.server_principals RoleP' +
  //      CHAR(10) + char(9) + 'On RoleP.principal_id = RM.role_principal_id' + CHAR(10) +
		//'Inner Join ' + QUOTENAME(@PartnerServer) + '.master.sys.server_principals LoginP' +
  //      CHAR(10) + char(9) + 'On LoginP.principal_id = RM.member_principal_id' + CHAR(10) +
		//'Where LoginP.type In (''U'', ''G'', ''S'')' + CHAR(10) +
		//'And LoginP.name <> ''sa''' + CHAR(10) +
		//'And LoginP.name Not Like ''##%''' + CHAR(10) +
		//'And RoleP.type = ''R''' + CHAR(10) +
		//'And CharIndex(''' + @Machine + '\'', LoginP.name) = 0;';

    class Role
    {
        const string NAME = "name";
        const string ROLE = "role";

        public readonly string name;
        public readonly string role;

        public Role(object name, object role) : this((string)name, (string)role) { }
        public Role(string name, string role)
        {
            this.name = name;
            this.role = role;
        }

        public bool create(SqlConnection connection)
        {
            try
            {
                SqlCommand command = createCommand;
                command.Connection = connection;
                command.ExecuteNonQuery();
                return true;
            }catch(Exception e)
            {
                Log.f(e);
                Reports.add("Fatal", "Error Creating Role:", this.objectToString(), "<Br>", e.objectToString());
            }
            return false;
        }
        SqlCommand createCommand
        {
            get
            {
                SqlCommand command = new SqlCommand("sp_addsrvrolemember");
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@loginame", name);
                command.Parameters.AddWithValue("@rolename", role);
                
                return command;
            }
        }
        public static Role from(SqlDataReader reader)
        {
            return new Role(reader[NAME], reader[ROLE]);
        }

        //Select RoleP.name, LoginP.name
        //From master.sys.server_role_members RM

        //Inner Join master.sys.server_principals RoleP On RoleP.principal_id = RM.role_principal_id

        //Inner Join master.sys.server_principals LoginP On LoginP.principal_id = RM.member_principal_id

        public static Dictionary<string, List<Role>> from(SqlConnection connection)
        {
            Dictionary<string, List<Role>> roles = new Dictionary<string, List<Role>>();
            StringBuilder builder = new StringBuilder("Select RoleP.name as Role, LoginP.name as Name From master.sys.server_role_members RM Inner Join master.sys.server_principals RoleP On RoleP.principal_id = RM.role_principal_id Inner Join master.sys.server_principals LoginP On LoginP.principal_id = RM.member_principal_id;");
            SqlCommand com = new SqlCommand(builder.ToString(), connection);
            using (SqlDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    Role role = from(reader);
                    if (!roles.ContainsKey(role.name))
                    {
                        roles.Add(role.name, new List<Role>());
                    }

                    roles[role.name].Add(role);
                }
            }
            return roles;
        }
    }
}
