using Microsoft.Synchronization;
using System;
using FinLib.Logger;
using System.Data.SqlClient;
using System.Configuration;
using static FinLib.Logger.Types;
using System.Collections.Generic;
using DBSync.Model;

namespace DBSync
{
    class Program
    {
        static void Main(string[] args)
        {
            Log.registerDefaultType(new Log4Net("SystemLog"));

            try {
                DBSyncOrchestrator syncOrchestrator = new DBSyncOrchestrator("Sync", "source", "destination");
                syncOrchestrator.execute();
            } catch (Exception e)
            {
                Reports.add("Fatal Exception", e.Message, "<br>", e.objectToString());
                Log.f(e);
            }
            finally
            {
                Reports.send();
            }

            Log.i("Done");
            if (Settings.Default.Debug)
            {
                System.Console.ReadLine();
            }
        }
    }

}
