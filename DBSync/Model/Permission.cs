using FinLib.Logger;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBSync.Model
{

    //  Select P.name,
    //          SP.state_desc, SP.permission_name, SP.class, SP.class_desc, SP.major_id
    //      From master.sys.server_principals P

    //      Inner Join master.sys.server_permissions SP

    //      On SP.grantee_principal_id = P.principal_id
    //      Left Join master.sys.server_principals SubP

    //      On SubP.principal_id = SP.major_id And SP.class = 101
    //Left Join master.sys.endpoints SubEP On SubEP.endpoint_id = SP.major_id And SP.class = 105
    class Permission
    {
        const String NAME = "name", STATE_DESC = "state_desc", PERMISSION_NAME = "permission_name", CLASS = "class", CLASS_DESC = "class_desc", MAJOR_ID = "major_id", SUB_LOGIN_NAME = "sub_login_name", SUB_END_POINT_NAME = "sub_end_point_name";

        public readonly string name;
        public readonly string stateDesc;
        public readonly string permissionName;
        public readonly string classValue;
        public readonly string classDesc;
        //public readonly string majorId;
        public readonly string subLoginName;
        public readonly string subEndPointName;

        public Permission(object name, object stateDesc, object permissionName, object classValue, object classDesc, object majorId, object subLoginName, object subEndPointName) : this(name.objectToString(), stateDesc.objectToString(), permissionName.objectToString(), classValue.objectToString(), classDesc.objectToString(), majorId.objectToString(), subLoginName.objectToString(), subEndPointName.objectToString()) { }
        public Permission(string name, string stateDesc, string permissionName, string classValue, string classDesc, string majorId, string subLoginName, string subEndPointName)
        {
            this.name = name;
            this.stateDesc = stateDesc;
            this.permissionName = permissionName;
            this.classValue = classValue;
            this.classDesc = classDesc;
           // this.majorId = majorId;
            this.subLoginName = subLoginName;
            this.subEndPointName = subEndPointName;
        }

        public bool create(SqlConnection connection)
        {
            try
            {
                new SqlCommand(createStatement, connection).ExecuteNonQuery();
                return true;
            }catch(Exception e)
            {
                Log.f(e);
                Reports.add("Fatal", "Error Adding User Permission", this.objectToString(), "<br>", e.objectToString());
            }
            return false;
        }
        String createStatement
        {
            get
            {
                StringBuilder builder = new StringBuilder();

                builder.Append(stateDesc)
                    .Append(" ")
                    .Append(permissionName);

                switch (classValue)
                {
                    case "101":
                        builder.Append(" On Login::")
                            .Append("'")
                            .Append(subLoginName)
                            .Append("'");
                        break;

                    case "105":
                        builder.Append(" On ")
                            .Append(classDesc)
                            .Append("::")
                            .Append("'")
                            .Append(subEndPointName)
                            .Append("'");
                        break;
                }

                builder.Append(" To ")
                    .Append("[")
                    .Append(name)
                    .Append("];");

                return builder.ToString();
            }
        }
        public static Permission from(SqlDataReader reader)
        {
            return new Permission(reader[NAME], reader[STATE_DESC], reader[PERMISSION_NAME], reader[CLASS], reader[CLASS_DESC], reader[MAJOR_ID], reader[SUB_LOGIN_NAME], reader[SUB_END_POINT_NAME]);
        }

        public static Dictionary<string, List<Permission>> from(SqlConnection connection) {
            Dictionary<string, List<Permission>> permissions = new Dictionary<string, List<Permission>>();

            StringBuilder builder = new StringBuilder("Select P.name, SP.state_desc, SP.permission_name, SP.class, SP.class_desc, SP.major_id, SubP.name as sub_login_name, SubEP.name as sub_end_point_name From master.sys.server_principals P Inner Join master.sys.server_permissions SP On SP.grantee_principal_id = P.principal_id Left Join master.sys.server_principals SubP On SubP.principal_id = SP.major_id And SP.class = 101	Left Join master.sys.endpoints SubEP On SubEP.endpoint_id = SP.major_id And SP.class = 105;");
            SqlCommand com = new SqlCommand(builder.ToString(), connection);
            using (SqlDataReader reader = com.ExecuteReader())
            {
                while (reader.Read())
                {
                    Permission permission = from(reader);
                    if (!permissions.ContainsKey(permission.name))
                    {
                        permissions.Add(permission.name, new List<Permission>());
                    }

                    permissions[permission.name].Add(permission);
                }
            }

            return permissions;
        }
    }
}
