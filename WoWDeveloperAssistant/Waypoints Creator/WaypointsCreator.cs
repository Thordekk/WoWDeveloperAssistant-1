﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.DataVisualization.Charting;
using System.Windows.Forms;
using WoWDeveloperAssistant.Misc;
using static WoWDeveloperAssistant.Misc.Utils;
using static WoWDeveloperAssistant.Packets;

namespace WoWDeveloperAssistant.Waypoints_Creator
{
    public class WaypointsCreator
    {
        private MainForm mainForm;
        private BuildVersions buildVersion;
        private Dictionary<string, Creature> creaturesDict = new Dictionary<string, Creature>();

        public WaypointsCreator(MainForm mainForm)
        {
            this.mainForm = mainForm;
        }

        public bool GetDataFromSniffFile(string fileName)
        {
            var lines = File.ReadAllLines(fileName);
            Dictionary<long, PacketTypes> packetIndexes = new Dictionary<long, PacketTypes>();

            buildVersion = LineGetters.GetBuildVersion(lines);
            if (buildVersion == BuildVersions.BUILD_UNKNOWN)
            {
                MessageBox.Show(fileName + " has non-supported build.", "File Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                return false;
            }

            Parallel.For(0, lines.Length, index =>
            {
                if (lines[index].Contains("SMSG_UPDATE_OBJECT") &&
                !packetIndexes.ContainsKey(index))
                {
                    lock (packetIndexes)
                        packetIndexes.Add(index, PacketTypes.SMSG_UPDATE_OBJECT);
                }
                else if (lines[index].Contains("SMSG_SPELL_START") &&
                !packetIndexes.ContainsKey(index))
                {
                    lock (packetIndexes)
                        packetIndexes.Add(index, PacketTypes.SMSG_SPELL_START);
                }
                else if (lines[index].Contains("SMSG_ON_MONSTER_MOVE") &&
                !packetIndexes.ContainsKey(index))
                {
                    lock (packetIndexes)
                        packetIndexes.Add(index, PacketTypes.SMSG_ON_MONSTER_MOVE);
                }
            });

            creaturesDict.Clear();

            Parallel.ForEach(packetIndexes.AsEnumerable(), value =>
            {
                if (value.Value == PacketTypes.SMSG_UPDATE_OBJECT)
                {
                    Parallel.ForEach(ParseObjectUpdatePacket(lines, value.Key, buildVersion).AsEnumerable(), packet =>
                    {
                        lock (creaturesDict)
                        {
                            if (!creaturesDict.ContainsKey(packet.creatureGuid))
                            {
                                creaturesDict.Add(packet.creatureGuid, new Creature(packet));
                            }
                            else
                            {
                                creaturesDict[packet.creatureGuid].UpdateCreature(packet);
                            }
                        }
                    });
                }
            });

            foreach (var index in packetIndexes)
            {
                if (index.Value == PacketTypes.SMSG_ON_MONSTER_MOVE)
                {
                    MonsterMovePacket movePacket = ParseMovementPacket(lines, index.Key, buildVersion);
                    if (movePacket.creatureGuid == "")
                        continue;

                    if (creaturesDict.ContainsKey(movePacket.creatureGuid))
                    {
                        Creature creature = creaturesDict[movePacket.creatureGuid];

                        if (!creature.HasWaypoints() && movePacket.HasWaypoints())
                        {
                            foreach (Waypoint wp in movePacket.waypoints)
                            {
                                creature.waypoints.Add(wp);
                            }
                        }
                        else if (creature.HasWaypoints() && movePacket.HasOrientation())
                        {
                            creature.waypoints.Last().orientation = movePacket.creatureOrientation;
                            creature.waypoints.Last().orientationSetTime = movePacket.packetSendTime;
                        }
                        else if (creature.HasWaypoints() && movePacket.HasWaypoints())
                        {
                            if (creature.waypoints.Last().HasOrientation())
                            {
                                creature.waypoints.Last().delay = (uint)((movePacket.packetSendTime - creature.waypoints.Last().orientationSetTime).TotalMilliseconds);
                            }

                            foreach (Waypoint wp in movePacket.waypoints)
                            {
                                creature.waypoints.Add(wp);
                            }
                        }
                    }
                }
            }

            return true;
        }

        public void FillListBoxWithGuids()
        {
            mainForm.listBox_WC_CreatureGuids.Items.Clear();
            mainForm.grid_WC_Waypoints.Rows.Clear();

            foreach (Creature creature in creaturesDict.Values)
            {
                if (!creature.HasWaypoints())
                    continue;

                if (mainForm.toolStripTextBox_WC_Entry.Text != "" && mainForm.toolStripTextBox_WC_Entry.Text != "0")
                {
                    if (mainForm.toolStripTextBox_WC_Entry.Text == creature.entry.ToString() ||
                        mainForm.toolStripTextBox_WC_Entry.Text == creature.guid)
                    {
                        mainForm.listBox_WC_CreatureGuids.Items.Add(creature.guid);
                    }
                }
                else
                {
                    mainForm.listBox_WC_CreatureGuids.Items.Add(creature.guid);
                }
            }

            mainForm.listBox_WC_CreatureGuids.Refresh();
            mainForm.listBox_WC_CreatureGuids.Enabled = true;
        }

        public void FillWaypointsGrid()
        {
            if (mainForm.listBox_WC_CreatureGuids.SelectedItem == null)
                return;

            Creature creature = creaturesDict[mainForm.listBox_WC_CreatureGuids.SelectedItem.ToString()];

            mainForm.grid_WC_Waypoints.Rows.Clear();

            uint index = 1;

            foreach (Waypoint wp in creature.waypoints)
            {
                mainForm.grid_WC_Waypoints.Rows.Add(index, wp.movePosition.x, wp.movePosition.y, wp.movePosition.z, wp.orientation, wp.moveStartTime, wp.delay, false, wp);
                index++;
            }

            GraphPath();

            mainForm.grid_WC_Waypoints.Enabled = true;
        }

        public void GraphPath()
        {
            Creature creature = creaturesDict[mainForm.listBox_WC_CreatureGuids.SelectedItem.ToString()];

            mainForm.chart_WC.BackColor = Color.White;
            mainForm.chart_WC.ChartAreas[0].BackColor = Color.White;
            mainForm.chart_WC.ChartAreas[0].AxisX.ScaleView.ZoomReset();
            mainForm.chart_WC.ChartAreas[0].AxisY.ScaleView.ZoomReset();
            mainForm.chart_WC.ChartAreas[0].AxisY.IsReversed = true;
            mainForm.chart_WC.Titles.Clear();
            mainForm.chart_WC.Titles.Add(creature.name + " Entry: " + creature.entry);
            mainForm.chart_WC.Titles[0].Font = new Font("Arial", 16, FontStyle.Bold);
            mainForm.chart_WC.Titles[0].ForeColor = Color.Blue;
            mainForm.chart_WC.Titles.Add("Linked Id: " + creature.GetLinkedId());
            mainForm.chart_WC.Titles[1].Font = new Font("Arial", 16, FontStyle.Bold);
            mainForm.chart_WC.Titles[1].ForeColor = Color.Blue;
            mainForm.chart_WC.Series.Clear();
            mainForm.chart_WC.Series.Add("Path");
            mainForm.chart_WC.Series["Path"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Point;
            mainForm.chart_WC.Series.Add("Line");
            mainForm.chart_WC.Series["Line"].ChartType = System.Windows.Forms.DataVisualization.Charting.SeriesChartType.Spline;

            for (var i = 0; i < mainForm.grid_WC_Waypoints.RowCount; i++)
            {
                double posX = Convert.ToDouble(mainForm.grid_WC_Waypoints[1, i].Value);
                double posY = Convert.ToDouble(mainForm.grid_WC_Waypoints[2, i].Value);

                mainForm.chart_WC.Series["Path"].Points.AddXY(posX, posY);
                mainForm.chart_WC.Series["Path"].Points[i].Color = Color.Blue;
                mainForm.chart_WC.Series["Path"].Points[i].Label = Convert.ToString(i + 1);
                mainForm.chart_WC.Series["Line"].Points.AddXY(posX, posY);
                mainForm.chart_WC.Series["Line"].Points[i].Color = Color.Cyan;
            }
        }

        public void CutFromGrid()
        {
            foreach (DataGridViewRow row in mainForm.grid_WC_Waypoints.SelectedRows)
            {
                mainForm.grid_WC_Waypoints.Rows.Remove(row);
            }

            for (int i = 0; i < mainForm.grid_WC_Waypoints.Rows.Count; i++)
            {
                mainForm.grid_WC_Waypoints[0, i].Value = i + 1;
            }

            GraphPath();
        }

        public void CreateSQL()
        {
            Creature creature = creaturesDict[mainForm.listBox_WC_CreatureGuids.SelectedItem.ToString()];
            DataSet creatureAddonDs;
            string sqlQuery = "SELECT * FROM `creature_addon` WHERE `linked_id` = '" + creature.GetLinkedId() + "';";
            string creatureAddon = "";
            bool addonFound = false;
            creatureAddonDs = SQLModule.DatabaseSelectQuery(sqlQuery);

            if (creatureAddonDs != null && creatureAddonDs.Tables["table"].Rows.Count > 0)
            {
                creatureAddon = "UPDATE `creature_addon` SET `path_id` = @PATH WHERE `linked_id` = '" + creature.GetLinkedId() + "';" + "\r\n";
                addonFound = true;
            }
            else
            {
                creatureAddon = "('" + creature.GetLinkedId() + "', @PATH, 0, 0, 1, 0, 0, 0, 0, '', -1); " + "\r\n";
            }

            string SQLtext = "-- Pathing for " + creature.name + " Entry: " + creature.entry + "\r\n";
            SQLtext = SQLtext + "SET @GUID := (SELECT `guid` FROM `creature` WHERE `linked_id` = " + "'" + creature.GetLinkedId() + "'" + ");" + "\r\n";
            SQLtext = SQLtext + "SET @PATH := @GUID * 10;" + "\r\n";
            SQLtext = SQLtext + "UPDATE `creature` SET `spawndist` = 0, `MovementType` = 2 WHERE `linked_id` = '" + creature.GetLinkedId() + "'; " + "\r\n";

            if (addonFound)
            {
                SQLtext = SQLtext + creatureAddon;
            }
            else
            {
                SQLtext = SQLtext + "DELETE FROM `creature_addon` WHERE `linked_id` = '" + creature.GetLinkedId() + "';" + "\r\n";
                SQLtext = SQLtext + "INSERT INTO `creature_addon` (`linked_id`, `path_id`, `mount`, `bytes1`, `bytes2`, `emote`, `AiAnimKit`, `MovementAnimKit`, `MeleeAnimKit`, `auras`, `VerifiedBuild`) VALUES" + "\r\n";
                SQLtext = SQLtext + creatureAddon;
            }

            SQLtext = SQLtext + "DELETE FROM `waypoint_data` WHERE `id` = @PATH;" + "\r\n";
            SQLtext = SQLtext + "INSERT INTO `waypoint_data` (`id`, `point`, `position_x`, `position_y`, `position_z`, `orientation`, `delay`, `move_type`, `action`, `action_chance`, `speed`) VALUES" + "\r\n";

            for (int i = 0; i < mainForm.grid_WC_Waypoints.RowCount; i++)
            {
                if (i < (mainForm.grid_WC_Waypoints.RowCount - 1))
                {
                    SQLtext = SQLtext + "(@PATH, " + (i + 1) + ", " + mainForm.grid_WC_Waypoints[1, i].Value + ", " + mainForm.grid_WC_Waypoints[2, i].Value + ", " + mainForm.grid_WC_Waypoints[3, i].Value + ", " + mainForm.grid_WC_Waypoints[4, i].Value + ", " + mainForm.grid_WC_Waypoints[6, i].Value + ", 0" + ", 0" + ", 100" + ", 0" + "),\r\n";
                }
                else
                {
                    SQLtext = SQLtext + "(@PATH, " + (i + 1) + ", " + mainForm.grid_WC_Waypoints[1, i].Value + ", " + mainForm.grid_WC_Waypoints[2, i].Value + ", " + mainForm.grid_WC_Waypoints[3, i].Value + ", " + mainForm.grid_WC_Waypoints[4, i].Value + ", " + mainForm.grid_WC_Waypoints[6, i].Value + ", 0" + ", 0" + ", 100" + ", 0" + ");\r\n";
                }
            }

            SQLtext = SQLtext + "-- " + creature.guid + " .go " + creature.spawnPosition.x + " " + creature.spawnPosition.y + " " + creature.spawnPosition.z + "\r\n";

            if (Properties.Settings.Default.Vector)
            {
                SQLtext += "\r\n";
                SQLtext = SQLtext + "G3D::Vector3 const Path_XXX[" + mainForm.grid_WC_Waypoints.RowCount + "] =" + "\r\n";
                SQLtext = SQLtext + "{" + "\r\n";

                for (var i = 0; i < mainForm.grid_WC_Waypoints.RowCount; i++)
                {
                    if (i < (mainForm.grid_WC_Waypoints.RowCount - 1))
                    {
                        SQLtext = SQLtext + "{ " + mainForm.grid_WC_Waypoints[1, i].Value + "f, " + mainForm.grid_WC_Waypoints[2, i].Value + "f, " + mainForm.grid_WC_Waypoints[3, i].Value + "f },\r\n";
                    }
                    else
                    {
                        SQLtext = SQLtext + "{ " + mainForm.grid_WC_Waypoints[1, i].Value + "f, " + mainForm.grid_WC_Waypoints[2, i].Value + "f, " + mainForm.grid_WC_Waypoints[3, i].Value + "f };\r\n";
                    }
                }

                SQLtext = SQLtext + "};" + "\r\n";

                mainForm.textBox_SQLOutput.Text = SQLtext;
            }
        }

        public void RemoveNearestPoints()
        {
            bool canLoop = true;

            do
            {
                foreach (DataGridViewRow row in mainForm.grid_WC_Waypoints.Rows)
                {
                    Waypoint currentWaypoint = (Waypoint)row.Cells[8].Value;
                    Waypoint nextWaypoint;
                    try
                    {
                        nextWaypoint = (Waypoint)mainForm.grid_WC_Waypoints.Rows[row.Index + 1].Cells[8].Value;
                    }
                    catch
                    {
                        canLoop = false;
                        break;
                    }

                    if (currentWaypoint.movePosition.GetExactDist2d(nextWaypoint.movePosition) <= 5.0f &&
                        !nextWaypoint.HasOrientation())
                    {
                        mainForm.grid_WC_Waypoints.Rows.RemoveAt(row.Index + 1);
                        break;
                    }
                }
            }
            while (canLoop);

            for (int i = 0; i < mainForm.grid_WC_Waypoints.Rows.Count; i++)
            {
                mainForm.grid_WC_Waypoints[0, i].Value = i + 1;
            }

            GraphPath();
        }

        public void RemoveDuplicatePoints()
        {
            List<Waypoint> waypoints = new List<Waypoint>();
            List<string> hashList = new List<string>();

            foreach (DataGridViewRow row in mainForm.grid_WC_Waypoints.Rows)
            {
                Waypoint waypoint = (Waypoint)row.Cells[8].Value;
                string hash = SHA1HashStringForUTF8String(Convert.ToString(Math.Round(float.Parse(row.Cells[1].Value.ToString()) / 0.25), CultureInfo.InvariantCulture) + " " + Convert.ToString(Math.Round(float.Parse(row.Cells[2].Value.ToString()) / 0.25), CultureInfo.InvariantCulture) + " " + Convert.ToString(Math.Round(float.Parse(row.Cells[3].Value.ToString()) / 0.25), CultureInfo.InvariantCulture));

                if (!hashList.Contains(hash) || waypoint.HasOrientation())
                {
                    hashList.Add(hash);
                    waypoints.Add(waypoint);
                }
            }

            mainForm.grid_WC_Waypoints.Rows.Clear();

            uint index = 1;

            foreach (Waypoint wp in waypoints)
            {
                mainForm.grid_WC_Waypoints.Rows.Add(index, wp.movePosition.x, wp.movePosition.y, wp.movePosition.z, wp.orientation, wp.moveStartTime, wp.delay, false, wp);
                index++;
            }

            GraphPath();
        }

        public void CreateReturnPath()
        {
            List<Waypoint> waypoints = new List<Waypoint>();

            foreach (DataGridViewRow row in mainForm.grid_WC_Waypoints.Rows)
            {
                waypoints.Add((Waypoint)row.Cells[8].Value);
            }

            waypoints.Reverse();

            waypoints.RemoveAt(0);
            waypoints.RemoveAt(waypoints.Count - 1);

            int index = mainForm.grid_WC_Waypoints.Rows.Count + 1;

            foreach (Waypoint wp in waypoints)
            {
                mainForm.grid_WC_Waypoints.Rows.Add(index, wp.movePosition.x, wp.movePosition.y, wp.movePosition.z, wp.orientation, wp.moveStartTime, wp.delay, false, wp);
                index++;
            }

            GraphPath();
        }

        public uint GetCreatureEntryByGuid(string creatureGuid)
        {
            if (creaturesDict.ContainsKey(creatureGuid))
                return creaturesDict[creatureGuid].entry;

            return 0;
        }

        public void OpenFileDialog()
        {
            mainForm.openFileDialog.Title = "Open File";
            mainForm.openFileDialog.Filter = "Parsed Sniff File (*.txt)|*.txt";
            mainForm.openFileDialog.FileName = "*.txt";
            mainForm.openFileDialog.FilterIndex = 1;
            mainForm.openFileDialog.ShowReadOnly = false;
            mainForm.openFileDialog.Multiselect = false;
            mainForm.openFileDialog.CheckFileExists = true;
        }

        public void ImportStarted()
        {
            mainForm.Cursor = Cursors.WaitCursor;
            mainForm.toolStripButton_WC_LoadSniff.Enabled = false;
            mainForm.toolStripButton_WC_Search.Enabled = false;
            mainForm.toolStripTextBox_WC_Entry.Enabled = false;
            mainForm.listBox_WC_CreatureGuids.Enabled = false;
            mainForm.listBox_WC_CreatureGuids.Items.Clear();
            mainForm.listBox_WC_CreatureGuids.DataSource = null;
            mainForm.grid_WC_Waypoints.Enabled = false;
            mainForm.grid_WC_Waypoints.Rows.Clear();
            mainForm.toolStripStatusLabel_FileStatus.Text = "Loading File...";
        }

        public void ImportSuccessful()
        {
            mainForm.toolStripButton_WC_LoadSniff.Enabled = true;
            mainForm.toolStripButton_WC_Search.Enabled = true;
            mainForm.toolStripTextBox_WC_Entry.Enabled = true;
            mainForm.toolStripStatusLabel_FileStatus.Text = mainForm.openFileDialog.FileName + " is selected for input.";
            mainForm.Cursor = Cursors.Default;
        }
    }
}