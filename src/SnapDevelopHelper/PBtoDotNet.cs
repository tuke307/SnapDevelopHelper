using SnapDevelopHelper;
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.IO;
using System.Linq;

namespace SnapDevelop.Helper
{
    public class PBtoDotNet
    {
        #region XML

        private const string Comment = "///";
        private const string KeyAttribute = "[Key]";
        private const string Inheritdoc = "/// <inheritdoc />";
        private const string TableAttribute = "[Table(";
        private const string ColumnAttribute = "[SqlColumn(";

        private List<string> SummaryXML = new List<string>()
            {
                "/// <summary>",
                 "/// {0}.",
                 "/// </summary>"
            };

        #endregion XML

        #region DB

        private OdbcConnection DbConnection;

        private OdbcCommand DbCommand;

        private OdbcDataReader DbReader;
        private string Table;

        private string SqlComments =
             @"SELECT
               pbc_cnam as 'Spalte',
               pbc_cmnt as 'Kommentar'
               FROM
               pbcatcol
               WHERE
               pbc_tnam = '{0}'";

        #endregion DB

        private List<string> FileLines = new List<string>();
        private List<Pbcatcol> Properties = new List<Pbcatcol>();

        private void OpenConn()
        {
            DbConnection = new OdbcConnection(settings.Default.ConnectionString);
            DbConnection.Open();
        }

        private void SetTable()
        {
            // Zeile.
            int matchIndex = FileLines.FindIndex(x => x.Contains(TableAttribute));

            Table = Utils.GetBetween(FileLines[matchIndex], TableAttribute + '"', '"'.ToString());
        }

        private void GetComments()
        {
            DbCommand = DbConnection.CreateCommand();

            string sql = SqlComments;

            sql = string.Format(sql, Table);

            DbCommand.CommandText = sql;
            DbReader = DbCommand.ExecuteReader();

            while (DbReader.Read())
            {
                Pbcatcol pbcatcol = new Pbcatcol()
                {
                    Spalte = DbReader.GetString(0),
                    Kommentar = DbReader.SafeGetString(1)
                };

                // alle ausgeschriebenen umbrüche entfernen.
                pbcatcol.Kommentar.Replace("~r", "");
                pbcatcol.Kommentar.Replace("~n", "");
                pbcatcol.Kommentar.Replace("\r", "");
                pbcatcol.Kommentar.Replace("\n", "");

                Properties.Add(pbcatcol);
            }
        }

        public void RestructureProperties()
        {
            Pbcatcol property;
            int searchstartIndex = 0;
            bool commentAlreadyExist = false;

            // spaces entfernen
            for (int i = 0; i < FileLines.Count; i++)
            {
                FileLines[i] = FileLines[i].TrimStart();
            }

            do
            {
                int matchIndex = FileLines.FindIndex(searchstartIndex, FileLines.Count() - searchstartIndex, x => x.Contains(ColumnAttribute));

                // bei letzter property wird nichts mehr gefunden
                if (matchIndex == -1)
                    break;

                string propertyName = Utils.GetBetween(FileLines[matchIndex], ColumnAttribute + '"', '"'.ToString());

                // da string in string: \"property\"
                propertyName = propertyName.Trim('\"');

                // start und ende ermitteln
                int startIndex = FileLines.LastIndexOf(string.Empty, matchIndex) + 1;
                int endIndex = FileLines.IndexOf(string.Empty, matchIndex);

                // gibt es ein kommentar?
                var match = Properties.Where(x => x.Spalte.Contains(propertyName));
                if (match.Any())
                    property = match.First();
                else
                    property = null;

                // wenn letzte property
                if (endIndex == -1)
                {
                    endIndex = FileLines.IndexOf("}", matchIndex);
                }

                List<string> propertyLines = FileLines.GetRange(startIndex, endIndex - startIndex);

                // wenn erste property
                if (propertyLines.Where(x => x.Contains("namespace")).Any())
                {
                    startIndex = FileLines.LastIndexOf("{", matchIndex) + 1;

                    propertyLines = FileLines.GetRange(startIndex, endIndex - startIndex);
                }

                int endSummary = propertyLines.IndexOf(SummaryXML[2]);

                if (endSummary != -1)
                {
                    propertyLines.RemoveRange(0, endSummary + 1);

                    propertyLines.Insert(0, Inheritdoc);
                }

                #region Kommentar

                // wsummary tag schon vorhanden?
                var matches = propertyLines.Where(stringToCheck => stringToCheck.Contains(Comment, StringComparison.OrdinalIgnoreCase));
                commentAlreadyExist = matches.Any();

                if (commentAlreadyExist)
                {
                    if (settings.Default.DeleteCurrentComment)
                        propertyLines.RemoveRange(0, matches.Count());
                }

                // Kommentar einfügen
                if (settings.Default.DeleteCurrentComment && !commentAlreadyExist)
                {
                    List<string> summaryXMLeditable = new List<string>(SummaryXML);
                    summaryXMLeditable[1] = string.Format(summaryXMLeditable[1], property?.Kommentar);
                    propertyLines.InsertRange(0, summaryXMLeditable);
                }

                #endregion Kommentar

                #region EigenschaftsName

                // namenszeile
                string propertyLine = propertyLines.Last();
                // name zusammenbauen
                string newpropertyName = char.ToUpper(Table[0]) + Table.Substring(1).ToLower() +
                                        "_" +
                                      char.ToUpper(propertyName[0]) + propertyName.Substring(1).ToLower();

                #endregion EigenschaftsName

                #region EigenschaftsDatentyp

                // name ändern
                // 0. => zugriffsmodifziere (public)
                // 1. => datentyp
                // 2. => name
                var list = propertyLine.Split(' ').ToList();

                switch (list[1])
                {
                    case "Int16":
                        list[1] = "int";
                        break;

                    case "Int32":
                        list[1] = "int";
                        break;

                    case "String":
                        list[1] = "string";
                        break;

                    case "Decimal":
                        list[1] = "decimal";
                        break;

                    case "Double":
                        list[1] = "double";
                        break;

                    case "Byte":
                        list[1] = "byte";
                        break;
                }

                // nullable
                if (list[1].Last() != '?')
                {
                    if (settings.Default.PropertiesNullable && !propertyLines.Contains(KeyAttribute))
                        list[1] = list[1] + "?";

                    if (settings.Default.KeysNullable && propertyLines.Contains(KeyAttribute))
                        list[1] = list[1] + "?";
                }

                list[2] = newpropertyName;
                propertyLine = string.Join(" ", list);

                #endregion EigenschaftsDatentyp

                // property line ersetzen
                propertyLines.Remove(propertyLines.Last());
                propertyLines.Add(propertyLine);

                // Property inklusive Kommentar einfügen
                FileLines.RemoveRange(startIndex, endIndex - startIndex);
                FileLines.InsertRange(startIndex, propertyLines);

                // nächstes suchen
                searchstartIndex = matchIndex + 1;

                // kommentar neu hinzugefügt => um 3 erhöhen
                if (!commentAlreadyExist)
                    searchstartIndex += 3;
            } while (true && searchstartIndex < FileLines.Count());

            File.WriteAllLines(settings.Default.FilePath, FileLines);
        }

        public PBtoDotNet()
        {
            FileLines = File.ReadAllLines(settings.Default.FilePath).ToList();

            SetTable();
            OpenConn();
            GetComments();
            RestructureProperties();
        }

        ~PBtoDotNet()
        {
            DbReader.Close();
            DbCommand.Dispose();
            DbConnection.Close();
        }
    }
}