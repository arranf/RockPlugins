// <copyright>
// Copyright Southeast Christian Church
//
// Licensed under the  Southeast Christian Church License (the "License");
// you may not use this file except in compliance with the License.
// A copy of the License shoud be included with this file.
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//
// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Entity;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Web.UI;
using System.Web.UI.HtmlControls;
using System.Web.UI.WebControls;
using Newtonsoft.Json;
using Rock;
using Rock.Attribute;
using Rock.Data;
using Rock.Model;
using Rock.Security;
using Rock.Web.Cache;
using Rock.Web.UI.Controls;

namespace org.secc.Connection
{
    /// <summary>
    /// Block that can load sample data into your Rock database.
    /// Dev note: You can set the XML Document Url setting to your local
    /// file when you're testing new data.  Something like C:\Misc\Rock\Documentation\sampledata.xml
    /// </summary>
    [DisplayName( "Volunteer Signup Wizard" )]
    [Category( "SECC > Connection" )]
    [Description( "Block for configuring and outputing a configurable page for setting numbers of volunteers needed." )]
    
    [CodeEditorField("Settings", mode:CodeEditorMode.JavaScript, category: "Custom Setting")]
    [KeyValueListField("Counts", category: "Custom Setting" )]
    [CodeEditorField( "Lava", mode: CodeEditorMode.JavaScript, category: "Custom Setting", defaultValue:
@"<link rel=""stylesheet"" type=""text/css"" href=""/Plugins/org_secc/Connection/VolunteerSignupWizard.css"" />
{%- comment -%}
    Select from one of the following templates that come prebuilt for you with the Signup Wizard and set the
    output variable below to the appropriate value
    
    Genius     - This will output as a structured table similar to other signup systems out there
    CardPage   - This will output as a single page with panels containing cards.  This is probably best
                 for outputting 1-2 partitions
    CardWizard - This is good for fairly complex signups with 2-4 partitions (Campus, DefinedType, Role, Schedule).
                 It will output and behave in a left-to-right animated set of cards and allow for signing up for
                 multiple roles or attributes at once.

    This is setup to encourage you to copy existing lava templates if you make modifications rather than modifying the
    default ones which come with the Signup Wizard plugin.
{%- endcomment -%}

{%- assign output = ""Genius"" -%}

{% if output == ""Genius"" %}
{% include '~/Plugins/org_secc/Connection/VolunteerGenius.lava' %}
{% elseif output == ""CardPage"" %}
{% include '~/Plugins/org_secc/Connection/CardPage.lava' %}
{% elseif output == ""CardWizard"" %}
{% include '~/Plugins/org_secc/Connection/CardWizard.lava' %}
{% endif %}
" )]
    [BooleanField( "Enable Debug", "Display a list of merge fields available for lava.", false)]

    [ViewStateModeById]
    public partial class VolunteerSignupWizard : Rock.Web.UI.RockBlockCustomSettings
    {

        private Dictionary<string, string> attributes = new Dictionary<string, string>();

        private ICollection<ConnectionRequest> connectionRequests = null;


        protected SignupSettings Settings {
            get {
                var settings = GetSetting<SignupSettings>("Settings");
                foreach(var p in settings.Partitions)
                {
                    p.SignupSettings = settings;
                }
                return settings;
                }
            set {
                ViewState["Settings"] = value;
                SaveViewState();
            }
        }
        
        protected Dictionary<string, string> Counts
        {
            get
            {
                return GetSetting<Dictionary<string, string>>( "Counts" );
            }
            set
            {
                ViewState["Counts"] = value;
                SaveViewState();
            }
        }

        private T GetSetting<T>( string key ) where T: new()
        {

            if ( ViewState[key] != null )
            {
                try
                {
                    return ( T ) ViewState[key];
                }
                catch ( Exception )
                {
                    // Just swallow this exception
                }
            }
            if ( !string.IsNullOrWhiteSpace(GetAttributeValue( key )) )
            {
                try
                {
                    if (BlockCache.Attributes[key].FieldType.Guid == Rock.SystemGuid.FieldType.KEY_VALUE_LIST.AsGuid())
                    {
                        ViewState[key] = GetAttributeValue( key ).AsDictionary();
                    }
                    else
                    {
                        ViewState[key] = GetAttributeValue( key ).FromJsonOrNull<T>();
                    }
                }
                catch ( Exception )
                {
                    // Just swallow this exception
                }
            }
            if ( ViewState[key] == null )
            {
                ViewState[key] = new T();
            }
            SaveViewState();
            return (T)ViewState[key];
        }


        #region Base Control Methods

        ////  overrides of the base RockBlock methods (i.e. OnInit, OnLoad)

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Init" /> event.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnInit( EventArgs e )
        {
            base.OnInit( e );

        }

        protected override void LoadViewState( object savedState )
        {
            base.LoadViewState( savedState );
            LoadSettings();
        }


        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Load" /> event.
        /// </summary>
        /// <param name="e">The <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnLoad( EventArgs e )
        {
            base.OnLoad( e );


            if ( Settings.Entity() == null )
            {
                nbConfigurationWarning.Visible = true;
                nbConfigurationWarning.Text = "The connection opportunities, partitions, and display settings need to be configured in block settings.";
            }

            if ( !IsPostBack )
            {
                ceLava.Text = GetAttributeValue( "Lava" );
            }

            if ( Settings.Partitions.Count > 0 )
            {
                ConnectionOpportunity connection = ( ConnectionOpportunity ) Settings.Entity();
                if ( connection != null && connectionRequests == null )
                {
                    connectionRequests = connection.ConnectionRequests;
                }

                var mergeFields = Rock.Lava.LavaHelper.GetCommonMergeFields( null );
                mergeFields.Add( "Settings", Rock.Lava.RockFilters.FromJSON( GetAttributeValue( "Settings" ) ) );
                string url = "";
                if ( Settings.SignupPage() != null )
                {
                    url = new Rock.Web.PageReference( Settings.SignupPage().Id ).BuildUrl();
                    if ( Settings.EntityTypeGuid == Rock.SystemGuid.EntityType.CONNECTION_OPPORTUNITY.AsGuid() )
                    {
                        url += "?OpportunityId=" + Settings.Entity().Id;
                    }
                }
                mergeFields.Add( "Tree", GetTree( Settings.Partitions.FirstOrDefault(), connectionRequests, parentUrl: url ) );
                mergeFields.Add( "ConnectionRequests", connectionRequests );
                lBody.Text = GetAttributeValue( "Lava" ).ResolveMergeFields(mergeFields);

                if ( GetAttributeValue( "EnableDebug" ).AsBoolean() && IsUserAuthorized( Authorization.EDIT ) )
                {
                    lDebug.Visible = true;
                    lDebug.Text = mergeFields.lavaDebugInfo();
                }
            }
        }

        protected override void OnPreRender( EventArgs e )
        {
            bddlAddPartition.SelectedValue = "";
        }

        #endregion

        
        /// <summary>
        /// Shows the settings.
        /// </summary>
        protected override void ShowSettings()
        {
            if ( Settings.Partitions.Count > 0 )
            {
                deactivateTabs();
                liCounts.AddCssClass( "active" );
                pnlCounts.Visible = true;
            }

            var connectionOpportunityService = new ConnectionOpportunityService( new RockContext() );
            var connections = connectionOpportunityService.Queryable().Where( co => co.IsActive == true ).OrderBy( co => co.ConnectionType.Name ).ThenBy( co => co.Name ).ToList()
                                                                    .Select( co => new ListItem( co.ConnectionType.Name + ": " + co.Name, co.Guid.ToString() ) ).ToList();
            connections.Insert( 0, new ListItem( "Select One . . ." ) );
            rddlConnection.DataSource = connections;
            rddlConnection.DataTextField = "Text";
            rddlConnection.DataValueField = "Value";
            rddlConnection.DataBind();

            if ( Settings.EntityGuid != Guid.Empty )
            {
                rddlConnection.SelectedValue = Settings.EntityGuid.ToString();
            }

            if ( Settings.EntityTypeGuid == Rock.SystemGuid.EntityType.CONNECTION_OPPORTUNITY.AsGuid() )
            {
                rddlType.SelectedValue = "Connection";
                rddlConnection.Visible = true;
            }
            else if ( Settings.EntityTypeGuid == Rock.SystemGuid.EntityType.GROUP.AsGuid() )
            {
                rddlType.SelectedValue = "Group";
                gpGroup.Visible = true;
            }


            mdEdit.Show();
        }
        
        private void UpdateCounts()
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach ( GridViewRow gridRow in gCounts.Rows )
            {
                string rowId = gCounts.DataKeys[gridRow.RowIndex].Value.ToString();

                RockTextBox textBox = ( RockTextBox ) gridRow.FindControl( "tb" + gridRow.ID );
                if (textBox != null)
                {
                    if ( values.ContainsKey( rowId ) )
                    {
                        values[rowId] = textBox.Text;
                    }
                    else
                    {
                        values.Add( rowId, textBox.Text );
                    }
                }
            }
            Counts = values;
            SaveViewState();
        }

        private void LoadSettings()
        {


            using ( var context = new RockContext() )
            {
                GroupTypeRoleService groupTypeRoleService = new GroupTypeRoleService( context );
                ScheduleService scheduleService = new ScheduleService( context );

                if ( Settings.SignupPage() != null)
                {
                    ppSignupPage.SetValue( Settings.SignupPage().Id );
                }

                // Load all the partition settings
                if ( Settings.EntityGuid != Guid.Empty )
                {
                    pnlPartitions.Visible = true;
                }

                rptPartions.DataSource = Settings.Partitions;
                rptPartions.DataBind();

                // Remove all existing dynamic columns
                while ( gCounts.Columns.Count  > 1)
                {
                    gCounts.Columns.RemoveAt( 0 );
                }
                DataTable dt = new DataTable();
                dt.Columns.Add( "RowId" );
                foreach ( var partition in Settings.Partitions )
                {
                    DataTable dtTmp = dt.Copy();
                    dt.Clear();
                    String column = partition.PartitionType;
                    if ( partition.PartitionType == "DefinedType" )
                    {
                        var definedType = Rock.Web.Cache.DefinedTypeCache.Read( partition.PartitionValue.AsGuid() );
                        if ( definedType == null)
                        {
                            break;
                        }
                        column = definedType.Name;
                    }
                    var boundField = new BoundField() { HeaderText = column, DataField = column + partition.Guid };
                    gCounts.Columns.Insert( gCounts.Columns.Count-1, boundField );
                    dt.Columns.Add( column + partition.Guid );
                    
                    switch ( partition.PartitionType )
                    {
                        case "DefinedType":

                            var definedType = Rock.Web.Cache.DefinedTypeCache.Read( partition.PartitionValue.AsGuid() );
                            foreach ( var value in definedType.DefinedValues )
                            {
                                AddRowColumnPartition( dtTmp, dt, column + partition.Guid, value.Guid, value.Value );
                            }
                            break;
                        case "Campus":
                            if ( partition.PartitionValue != null)
                            { 
                                var selectedCampuses = partition.PartitionValue.Split( ',' );
                                foreach ( string campusGuid in selectedCampuses )
                                {
                                    var campus = CampusCache.Read( campusGuid.AsGuid() );
                                    if (campus!= null)
                                    {
                                        AddRowColumnPartition( dtTmp, dt, column + partition.Guid, campus.Guid, campus.Name );
                                    }
                                }
                            }
                            break;
                        case "Schedule":
                            if ( partition.PartitionValue != null )
                            { 
                                var selectedSchedules = partition.PartitionValue.Split( ',' );

                                foreach ( string scheduleGuid in selectedSchedules )
                                {
                                    var schedule = scheduleService.Get( scheduleGuid.AsGuid() );
                                    if ( schedule != null )
                                    {
                                        AddRowColumnPartition( dtTmp, dt, column + partition.Guid, schedule.Guid, schedule.Name );
                                    }
                                }
                            }
                            break;
                        case "Role":
                            if ( partition.PartitionValue != null )
                            {
                                var selectedRoles = partition.PartitionValue.Split( ',' );
                                List<GroupTypeRole> roles = new List<GroupTypeRole>();
                                foreach ( string roleGuid in selectedRoles )
                                {
                                    GroupTypeRole role = groupTypeRoleService.Get( roleGuid.AsGuid() );
                                    if ( role != null )
                                    {
                                        roles.Add( role );
                                    }
                                }
                                roles.OrderBy( r => r.GroupTypeId ).ThenBy( r => r.Order );

                                foreach ( GroupTypeRole role in roles )
                                {
                                    AddRowColumnPartition( dtTmp, dt, column + partition.Guid, role.Guid, role.Name );
                                }
                            }
                            break;

                    }
                }
                if ( Settings.Partitions.Count > 0 && dt.Rows.Count > 0 )
                {
                    var dv = dt.AsEnumerable();
                    var dvOrdered = dv.OrderBy( r => r.Field<String>( dt.Columns.Cast<DataColumn>().Select( c => c.ColumnName ).Skip( 1 ).FirstOrDefault() ) );
                    foreach ( var column in dt.Columns.Cast<DataColumn>().Select( c => c.ColumnName ).Skip( 2 ) )
                    {
                        dvOrdered = dvOrdered.ThenBy( r => r.Field<String>( column ) );
                        break;
                    }
                    dt = dvOrdered.CopyToDataTable();
                    gCounts.DataSource = dt;
                    gCounts.DataBind();

                }
            }
        }

        private void AddRowColumnPartition(DataTable source, DataTable target, String columnKey, Guid guid, String value)
        {
            if ( source.Rows.Count == 0 )
            {
                var newRow = target.NewRow();
                newRow["RowId"] = guid.ToString();
                target.Rows.Add( newRow );
                newRow[columnKey] = value;
            }
            foreach ( DataRow rowTmp in source.Rows )
            {
                var newRow = target.NewRow();
                foreach ( DataColumn columnTmp in source.Columns )
                {
                    newRow[columnTmp.ColumnName] = rowTmp[columnTmp.ColumnName];
                }
                newRow["RowId"] = rowTmp["RowId"] + "," + guid.ToString();
                newRow[columnKey] = value;
                target.Rows.Add( newRow );
            }
        }


        [Serializable]
        public class PartitionSettings
        {
            public string AttributeKey { get; set; }
            public string PartitionType { get; set; }
            public string PartitionValue { get; set; }
            public Guid Guid { get; set; }
            public Dictionary<string, string> GroupMap { get; set; }

            [JsonIgnore]
            public SignupSettings SignupSettings { get; set; }
            
            public PartitionSettings NextPartition { get
                {
                    if ( SignupSettings != null)
                    {
                        return SignupSettings.Partitions.SkipWhile( p => p != this ).Skip( 1 ).FirstOrDefault();
                    }
                    return null;
                }
            }
        }

        [Serializable]
        public class SignupSettings
        {
            public Guid SignupPageGuid { get; set; }

            private List<PartitionSettings> _partitions = new List<PartitionSettings>();

            public List<PartitionSettings> Partitions { get { return _partitions; } }

            public Guid EntityTypeGuid { get; set; }

            public Guid EntityGuid { get; set; }

            /// <summary>
            /// Get the entity (group or connection) for this signup
            /// </summary>
            /// <param name="context"></param>
            /// <returns></returns>
            public virtual IModel Entity (RockContext context = null)
            {
                if (context == null)
                {
                    context = new RockContext();
                }
                if ( EntityTypeGuid == Rock.SystemGuid.EntityType.CONNECTION_OPPORTUNITY.AsGuid() )
                {
                    ConnectionOpportunityService connectionOpportunityService = new ConnectionOpportunityService( context );
                    return connectionOpportunityService.Get( EntityGuid );
                }

                if ( EntityTypeGuid == Rock.SystemGuid.EntityType.GROUP.AsGuid() )
                {
                    GroupService groupService = new GroupService( context );
                    return groupService.Get( EntityGuid );
                }
                return null;
            }
            
            /// <summary>
            /// Get the signup page
            /// </summary>
            /// <returns></returns>
            public virtual PageCache SignupPage()
            {
                return PageCache.Read( SignupPageGuid );
            }
        }

        #region Events

        protected void lbSettings_Click( object sender, EventArgs e )
        {
            UpdateCounts();
            deactivateTabs();
            liSettings.AddCssClass( "active" );
            pnlSettings.Visible = true;
        }

        protected void lbCounts_Click( object sender, EventArgs e )
        {
            UpdateCounts();
            deactivateTabs();
            liCounts.AddCssClass( "active" );
            pnlCounts.Visible = true;

        }

        protected void lbLava_Click( object sender, EventArgs e )
        {
            UpdateCounts();
            deactivateTabs();
            liLava.AddCssClass( "active" );
            pnlLava.Visible = true;
        }

        private void deactivateTabs()
        {
            liSettings.RemoveCssClass( "active" );
            liCounts.RemoveCssClass( "active" );
            liLava.RemoveCssClass( "active" );
            pnlSettings.Visible = false;
            pnlCounts.Visible = false;
            pnlLava.Visible = false;

        }

        protected List<Group> getGroups()
        {
            ConnectionOpportunity opportunity = ( ( ConnectionOpportunity ) Settings.Entity() );
            int[] campusIds = opportunity.ConnectionOpportunityCampuses.Select( o => o.CampusId ).ToArray();
            // Build list of groups
            var groups = new List<Group>();

            // First add any groups specifically configured for the opportunity 
            var opportunityGroupIds = opportunity.ConnectionOpportunityGroups.Select( o => o.Id ).ToList();
            if ( opportunityGroupIds.Any() )
            {
                groups = opportunity.ConnectionOpportunityGroups
                    .Where( g =>
                        g.Group != null &&
                        g.Group.IsActive &&
                        ( !g.Group.CampusId.HasValue || campusIds.Contains( g.Group.CampusId.Value ) ) )
                    .Select( g => g.Group )
                    .ToList();
            }

            // Then get any groups that are configured with 'all groups of type'
            foreach ( var groupConfig in opportunity.ConnectionOpportunityGroupConfigs )
            {
                if ( groupConfig.UseAllGroupsOfType )
                {
                    var existingGroupIds = groups.Select( g => g.Id ).ToList();

                    groups.AddRange( new GroupService( new RockContext() )
                        .Queryable().AsNoTracking()
                        .Where( g =>
                            !existingGroupIds.Contains( g.Id ) &&
                            g.IsActive &&
                            g.GroupTypeId == groupConfig.GroupTypeId &&
                            ( !g.CampusId.HasValue || campusIds.Contains(g.CampusId.Value) ) )
                        .ToList() );
                }
            }
            return groups;
        }

        protected void rptPartions_ItemDataBound( object sender, RepeaterItemEventArgs e )
        {
            if ( e.Item.ItemType == ListItemType.Item || e.Item.ItemType == ListItemType.AlternatingItem )
            {

                /*var ddlAttribute = ( DropDownList ) e.Item.FindControl( "ddlAttribute" );
                ddlAttribute.DataSource = attributes;
                ddlAttribute.DataTextField = "Value";
                ddlAttribute.DataValueField = "Key";
                ddlAttribute.DataBind();
                */

                var partition = ( ( PartitionSettings ) e.Item.DataItem );
                var phPartitionControl = ( PlaceHolder ) e.Item.FindControl( "phPartitionControl" );
                RockTextBox tbAttributeKey = ( ( RockTextBox ) e.Item.FindControl( "tbAttributeKey" ) );
                switch ( partition.PartitionType )
                {
                    case "Campus":
                        phPartitionControl.Controls.Add( new LiteralControl( "<div class='row'><div class='col-md-4'><strong>Name</strong></div><div class='col-md-8'><strong>Group</strong></div>" ) );

                        foreach ( var campus in CampusCache.All() )
                        {
                            phPartitionControl.Controls.Add( new LiteralControl( "<div class='row'><div class='col-xs-4'>" ) );
                            var campusCbl = new CheckBox();
                            campusCbl.ID = campus.Guid.ToString() + "_" + partition.Guid + "_checkbox";
                            if ( partition.PartitionValue != null )
                            {
                                campusCbl.Checked = partition.PartitionValue.Contains( campus.Guid.ToString() );
                            }
                            campusCbl.Text = campus.Name;
                            campusCbl.CheckedChanged += CampusCbl_CheckedChanged;
                            campusCbl.AutoPostBack = true;
                            if (partition.PartitionValue != null)
                            {
                                campusCbl.Checked = partition.PartitionValue.Contains( campus.Guid.ToString() );
                            }
                            phPartitionControl.Controls.Add( campusCbl );
                            phPartitionControl.Controls.Add( new LiteralControl( "</div>" ) );

                            phPartitionControl.Controls.Add( new LiteralControl( "<div class='col-xs-8'>" ) );
                            var ddlPlacementGroup = new RockDropDownList();
                            ddlPlacementGroup.ID = campus.Guid.ToString() + "_" + partition.Guid + "_group";
                            ddlPlacementGroup.SelectedIndexChanged += DdlPlacementGroup_SelectedIndexChanged;
                            ddlPlacementGroup.AutoPostBack = true;
                            List<ListItem> groupList = getGroups().Select( g => new ListItem( String.Format( "{0} ({1})", g.Name, g.Campus != null ? g.Campus.Name : "No Campus" ), g.Id.ToString() ) ).ToList();                  
                            groupList.Insert( 0, new ListItem( "Not Mapped", null ) );
                            ddlPlacementGroup.Items.AddRange( groupList.ToArray() );
                            if ( partition.GroupMap != null && partition.GroupMap.ContainsKey( campus.Guid.ToString() ) )
                            {
                                ddlPlacementGroup.SetValue( partition.GroupMap[campus.Guid.ToString()] );
                            }
                            phPartitionControl.Controls.Add( ddlPlacementGroup );
                            phPartitionControl.Controls.Add( new LiteralControl( "</div></div>" ) );
                        }
                        phPartitionControl.Controls.Add( new LiteralControl( "</div>" ) );
                        tbAttributeKey.ReadOnly = true;

                        break;
                    case "Schedule":
                        phPartitionControl.Controls.Add( new LiteralControl( "<strong>Schedule</strong><br />" ) );
                        var schedule = new SchedulePicker() { AllowMultiSelect = true, ID = partition.Guid.ToString() };
                        schedule.SelectItem += Schedule_SelectItem;
                        if ( !string.IsNullOrWhiteSpace(partition.PartitionValue) )
                        {
                            ScheduleService scheduleService = new ScheduleService( new RockContext() );
                            List<Guid> scheduleGuids = partition.PartitionValue.Split( ',' ).Select( pv => pv.AsGuid() ).ToList();
                            
                            schedule.SetValues( scheduleService.GetByGuids( scheduleGuids ).ToList().Select( s => s.Id ).ToList() );
                        }
                        phPartitionControl.Controls.Add( schedule );
                        break;
                    case "DefinedType":
                        phPartitionControl.Controls.Add( new LiteralControl( "<strong>Defined Type</strong><br />" ) );
                        var definedTypeRddl = new RockDropDownList() { ID = partition.Guid.ToString() };
                        DefinedTypeService definedTypeService = new DefinedTypeService( new RockContext() );
                        var listItems = definedTypeService.Queryable().Select( dt => new { Name = ( dt.Category != null ? dt.Category.Name + ": " : "" ) + dt.Name, Guid = dt.Guid } ).ToList();
                        listItems.Insert( 0, new { Name = "Select One . . .", Guid = Guid.Empty } );
                        definedTypeRddl.DataSource = listItems;
                        definedTypeRddl.DataTextField = "Name";
                        definedTypeRddl.DataValueField = "Guid";
                        definedTypeRddl.DataBind();
                        definedTypeRddl.AutoPostBack = true;
                        definedTypeRddl.SelectedIndexChanged += DefinedTypeRddl_SelectedIndexChanged;
                        if (!string.IsNullOrWhiteSpace(partition.PartitionValue))
                        {
                            definedTypeRddl.SelectedValue = partition.PartitionValue;
                        }
                        phPartitionControl.Controls.Add( definedTypeRddl );
                        break;
                    case "Role":
                        if ( Settings.EntityTypeGuid == Rock.SystemGuid.EntityType.CONNECTION_OPPORTUNITY.AsGuid() )
                        {
                            ConnectionOpportunity connection = ( ConnectionOpportunity ) Settings.Entity();
                            var roles = connection.ConnectionOpportunityGroupConfigs.Where( cogc => cogc.GroupMemberRole != null ).OrderBy( r => r.GroupTypeId ).ThenBy( r => r.GroupMemberRole.Order ).Select( r => new { Name = r.GroupType.Name + ": " + r.GroupMemberRole.Name, Guid = r.GroupMemberRole.Guid } ).ToList();

                            phPartitionControl.Controls.Add( new LiteralControl( "<div class='row'><div class='col-md-4'><strong>Name</strong></div><div class='col-md-8'><strong>Group</strong></div>" ) );

                            foreach ( var role in roles )
                            {

                                phPartitionControl.Controls.Add( new LiteralControl( "<div class='row'><div class='col-xs-4'>" ) );
                                var roleCbl = new CheckBox();
                                roleCbl.ID = role.Guid.ToString() + "_" + partition.Guid + "_checkbox";
                                if ( !string.IsNullOrWhiteSpace( partition.PartitionValue ) )
                                {
                                    roleCbl.Checked = partition.PartitionValue.Contains( role.Guid.ToString() );
                                }
                                roleCbl.Text = role.Name;
                                roleCbl.CheckedChanged += CampusCbl_CheckedChanged;
                                roleCbl.AutoPostBack = true;
                                phPartitionControl.Controls.Add( roleCbl );
                                phPartitionControl.Controls.Add( new LiteralControl( "</div>" ) );

                                phPartitionControl.Controls.Add( new LiteralControl( "<div class='col-xs-8'>" ) );
                                var ddlPlacementGroup = new RockDropDownList();
                                ddlPlacementGroup.ID = role.Guid.ToString() + "_" + partition.Guid + "_group";
                                ddlPlacementGroup.SelectedIndexChanged += DdlPlacementGroup_SelectedIndexChanged;
                                ddlPlacementGroup.AutoPostBack = true;

                                List<ListItem> groupList = getGroups().Select( g => new ListItem( String.Format( "{0} ({1})", g.Name, g.Campus != null ? g.Campus.Name : "No Campus" ), g.Id.ToString() ) ).ToList();
                                groupList.Insert( 0, new ListItem( "Not Mapped", null ) );
                                ddlPlacementGroup.Items.AddRange( groupList.ToArray() );
                                if ( partition.GroupMap != null && partition.GroupMap.ContainsKey( role.Guid.ToString() ) )
                                {
                                    ddlPlacementGroup.SetValue( partition.GroupMap[role.Guid.ToString()] );
                                }
                                phPartitionControl.Controls.Add( ddlPlacementGroup );
                                phPartitionControl.Controls.Add( new LiteralControl( "</div></div>" ) );
                            }
                            phPartitionControl.Controls.Add( new LiteralControl( "</div>" ) );
                            tbAttributeKey.ReadOnly = true;
                        }

                        break;
                }

                tbAttributeKey.ID = "attribute_key_" + partition.Guid.ToString();
                tbAttributeKey.Text = partition.AttributeKey;
                ( ( LinkButton ) e.Item.FindControl( "bbPartitionDelete" ) ).CommandArgument = partition.Guid.ToString();
            }
            
        }

        private void DdlPlacementGroup_SelectedIndexChanged( object sender, EventArgs e )
        {
            var partition = Settings.Partitions.Where( p => ( ( Control ) sender ).ID.Contains( p.Guid.ToString() ) ).FirstOrDefault();
            if ( partition != null )
            {
                string valueGuid = ( ( Control ) sender ).ID.Replace( partition.Guid.ToString() + "_", "" ).Replace( "_group", "" );
                if ( partition.GroupMap == null)
                {
                    partition.GroupMap = new Dictionary<string, string>();
                }
                partition.GroupMap.Remove( valueGuid );
                partition.GroupMap.Add( valueGuid, ( ( RockDropDownList ) sender ).SelectedValue );
            }
            SaveViewState();
        }

        private void CampusCbl_CheckedChanged( object sender, EventArgs e )
        {
            var partition = Settings.Partitions.Where( p => ( ( Control ) sender ).ID.Contains( p.Guid.ToString() ) ).FirstOrDefault();
            if ( partition != null )
            {
                string valueGuid = ( ( Control ) sender ).ID.Replace( partition.Guid.ToString() + "_", "" ).Replace( "_checkbox", "" );
                List<string> selectedValues = new List<string>();
                if ( partition.PartitionValue != null)
                {
                    selectedValues = partition.PartitionValue.Trim(',').Split( ',' ).ToList();
                }
                selectedValues.Remove( valueGuid );
                if ( ( ( CheckBox ) sender ).Checked)
                {
                    selectedValues.Add( valueGuid );
                }
                partition.PartitionValue = String.Join( ",", selectedValues );
            }
            SaveViewState();
        }

        protected void tbAttributeKey_TextChanged( object sender, EventArgs e )
        {
            var partition = Settings.Partitions.Where( p => p.Guid == ( ( Control ) sender ).ID.Replace("attribute_key_", "").AsGuid() ).FirstOrDefault();
            if ( partition != null )
            {
                partition.AttributeKey = ( ( TextBox ) sender ).Text;
            }
            SaveViewState();
        }

        protected void BddlAddPartition_SelectionChanged( object sender, EventArgs e )
        {
            if ( Counts.Count > 0 )
            {
                hdnPartitionType.Value = ( ( ButtonDropDownList ) sender ).SelectedValue;
                ScriptManager.RegisterStartupScript( upEditControls, upEditControls.GetType(), "PartitionWarning", "Rock.dialogs.confirm('Making changes to partition settings can affect existing counts!  Are you sure you want to proceed?', function(result) {if(result) {$(\"#" + btnAddPartition.ClientID + "\")[0].click();}});", true );
                return;
            }
            var partition = new PartitionSettings() { PartitionType = ( ( ButtonDropDownList ) sender ).SelectedValue, Guid = Guid.NewGuid(), SignupSettings = Settings };
            if ( partition.PartitionType == "Role" )
            {
                partition.AttributeKey = "GroupTypeRole";
            }
            else if ( partition.PartitionType == "Campus" )
            {
                partition.AttributeKey = "Campus";
            }
            Settings.Partitions.Add( partition );
            SaveViewState();
            rptPartions.DataSource = Settings.Partitions;
            rptPartions.DataBind();
        }

        protected void btnAddPartition_Click( object sender, EventArgs e )
        {
            var partition = new PartitionSettings() { PartitionType = hdnPartitionType.Value, Guid = Guid.NewGuid(), SignupSettings = Settings };
            if ( partition.PartitionType == "Role" )
            {
                partition.AttributeKey = "GroupTypeRole";
            }
            else if ( partition.PartitionType == "Campus" )
            {
                partition.AttributeKey = "Campus";
            }
            Settings.Partitions.Add( partition );
            SaveViewState();
            rptPartions.DataSource = Settings.Partitions;
            rptPartions.DataBind();
        }

        protected void bbPartitionDelete_Click( object sender, EventArgs e )
        {
            Settings.Partitions.Remove( Settings.Partitions.Where( p => p.Guid == ( ( LinkButton ) sender ).CommandArgument.AsGuid() ).FirstOrDefault() );
            SaveViewState();
            rptPartions.DataSource = Settings.Partitions;
            rptPartions.DataBind();
        }

        protected void GPicker_SelectItem( object sender, EventArgs e )
        {
            int groupId = ( ( GroupPicker ) ( ( HtmlAnchor ) sender ).Parent ).SelectedValue.AsInteger();
            GroupService groupService = new GroupService( new RockContext() );
            Settings.EntityGuid = groupService.Get( groupId ).Guid;
        }

        protected void ConnectionRddl_SelectedIndexChanged( object sender, EventArgs e )
        {
            Settings.EntityGuid = ( ( RockDropDownList ) sender ).SelectedValue.AsGuid();
            SaveViewState();
            pnlPartitions.Visible = true;
        }

        protected void RddlType_SelectedIndexChanged( object sender, EventArgs e )
        {
            gpGroup.Visible = false;
            rddlConnection.Visible = false;
            if ( ( ( RockDropDownList ) sender ).SelectedValue == "Group" )
            {
                gpGroup.Visible = true;
                Settings.EntityTypeGuid = Rock.SystemGuid.EntityType.GROUP.AsGuid();
            }
            else if ( ( ( RockDropDownList ) sender ).SelectedValue == "Connection" )
            {
                rddlConnection.Visible = true;
                Settings.EntityTypeGuid = Rock.SystemGuid.EntityType.CONNECTION_OPPORTUNITY.AsGuid();
            }
            SaveViewState();
        }


        protected void mdEdit_SaveClick( object sender, EventArgs e )
        {
            UpdateCounts();
            SetAttributeValue( "Settings", Settings.ToJson() );
            SetAttributeValue( "Counts", string.Join( "|", Counts.Select( a => a.Key + "^" + a.Value ).ToList() ) );
            SetAttributeValue( "Lava", ceLava.Text );
            SaveAttributeValues();
            mdEdit.Hide();
            Response.Redirect( Request.RawUrl );
        }

        private void Schedule_SelectItem( object sender, EventArgs e )
        {
            var partition = Settings.Partitions.Where( p => p.Guid == ( ( Control ) sender ).Parent.ID.AsGuid() ).FirstOrDefault();
            if ( partition != null )
            {
                List<int> scheduleIds = ( ( SchedulePicker ) ( ( Control ) sender ).Parent ).SelectedValues.Select( i => i.AsInteger() ).ToList();
                ScheduleService scheduleService = new ScheduleService( new RockContext() );
                partition.PartitionValue = String.Join(",", scheduleService.GetByIds( scheduleIds ).ToList().OrderBy(s => s.EffectiveStartDate.Value.ToString( "yyyyMMdd")).ThenBy(s => s.StartTimeOfDay).Select( s => s.Guid.ToString() ) );
            }
            SaveViewState();
        }

        private void DefinedTypeRddl_SelectedIndexChanged( object sender, EventArgs e )
        {
            var partition = Settings.Partitions.Where( p => p.Guid == ( ( Control ) sender ).ID.AsGuid() ).FirstOrDefault();
            if (partition != null)
            {
                partition.PartitionValue = ( ( RockDropDownList ) sender ).SelectedValue;
            }
            SaveViewState();
        }

        protected void gCounts_RowDataBound( object sender, GridViewRowEventArgs e )
        {
            if ( e.Row.RowType == DataControlRowType.DataRow )
            {
                string rowId = gCounts.DataKeys[e.Row.RowIndex].Value.ToString();
                RockTextBox textBox = new RockTextBox();
                textBox.Width = 60;
                textBox.Text = "";
                textBox.ID = "tb" + e.Row.ID;
                if ( Counts.ContainsKey( rowId ) )
                {
                    textBox.Text = Counts[rowId];
                }
                e.Row.Cells[gCounts.Columns.Count-1].Controls.Add( textBox );
            }
        }

        protected List<IDictionary<string, object>> GetTree( PartitionSettings partition, ICollection<ConnectionRequest> connectionRequests = null, String concatGuid = null, GroupTypeRoleService groupTypeRoleService = null, ScheduleService scheduleService = null, string ParentIdentifier = "signup", string parentUrl = "", string groupId = "", string roleId = "")
        {

            if ( groupTypeRoleService == null || scheduleService == null)
            {
                RockContext context = new RockContext();
                groupTypeRoleService = new GroupTypeRoleService( context );
                scheduleService = new ScheduleService( context );
            }
            var partitionList = new List<IDictionary<string, object>>();
            if ( partition.PartitionValue == null)
            {
                return null;
            }
            var values =  partition.PartitionValue.Trim(',').Split( ',' );

            if (partition.PartitionType == "DefinedType")
            {
                // Use every Defined Value 
                values = DefinedTypeCache.Read( partition.PartitionValue.AsGuid() ).DefinedValues.Select( dv => dv.Guid.ToString() ).ToArray();
            }

            foreach ( var value in values )
            {
                if (partition.PartitionType == "Role")
                {
                    roleId = value;
                }
                if (partition.GroupMap != null && partition.GroupMap.ContainsKey(value))
                {
                    groupId = partition.GroupMap[value];
                }
                string url = parentUrl + ( parentUrl.Contains( "?" ) ? "&" : "?" ) + partition.AttributeKey + "=" + value;
                IDictionary<string, object> inner = new Dictionary<string, object>();
                inner.Add( "ParentIdentifier", ParentIdentifier );
                inner.Add( "PartitionType", partition.PartitionType );
                inner.Add( "Url", url );
                inner.Add( "ParameterName", partition.AttributeKey );
                inner.Add( "Value", value );
                inner.Add( "RoleGuid", roleId );
                inner.Add( "GroupId", groupId );
                var newConcatGuid = concatGuid == null ? value : concatGuid + "," + value;
                int? count = Counts.Where( kvp => kvp.Key.Contains( newConcatGuid ) ).Any(kvp => String.IsNullOrWhiteSpace(kvp.Value))?null:(int?)(Counts.Where( kvp => kvp.Key.Contains( newConcatGuid ) ).Select( kvp => kvp.Value.AsInteger() ).Sum());
                inner.Add( "Limit", count );
                ICollection<ConnectionRequest> subRequests = null;
                switch ( partition.PartitionType )
                {
                    case "DefinedType":
                        inner["Entity"] = DefinedValueCache.Read( value.AsGuid() );
                        if ( connectionRequests != null)
                        {
                            subRequests = connectionRequests.Where( cr => cr.AssignedGroupMemberAttributeValues != null && cr.AssignedGroupMemberAttributeValues.Contains( value ) ).ToList();

                            inner.Add( "TotalFilled", this.connectionRequests.Where( cr => cr.AssignedGroupMemberAttributeValues != null && cr.AssignedGroupMemberAttributeValues.Contains( value ) && cr.ConnectionState != ConnectionState.Inactive ).Count() );
                        }
                        break;
                    case "Schedule":
                        inner["Entity"] = scheduleService.Get( value.AsGuid() );
                        if ( connectionRequests != null )
                        {
                            subRequests = connectionRequests.Where( cr => cr.AssignedGroupMemberAttributeValues != null && cr.AssignedGroupMemberAttributeValues.Contains( value ) ).ToList();

                            inner.Add( "TotalFilled", this.connectionRequests.Where( cr => cr.AssignedGroupMemberAttributeValues != null && cr.AssignedGroupMemberAttributeValues.Contains( value ) && cr.ConnectionState != ConnectionState.Inactive ).Count() );
                        }
                        break;
                    case "Campus":
                        var campus = CampusCache.Read( value.AsGuid() );
                        inner["Entity"] = campus;
                        if ( connectionRequests != null && campus != null )
                        {
                            subRequests = connectionRequests.Where( cr => cr.CampusId == campus.Id ).ToList();
                            inner.Add( "TotalFilled", this.connectionRequests.Where( cr => cr.CampusId == campus.Id && cr.ConnectionState != ConnectionState.Inactive ).Count() );

                        }
                        break;
                    case "Role":
                        var role = groupTypeRoleService.Get( value.AsGuid() );
                        inner["Entity"] = role;
                        if ( connectionRequests != null )
                        {
                            subRequests = connectionRequests.Where( cr => cr.AssignedGroupMemberRoleId == role.Id ).ToList();
                            inner.Add( "TotalFilled", this.connectionRequests.Where( cr => cr.AssignedGroupMemberRoleId == role.Id && cr.ConnectionState != ConnectionState.Inactive ).Count() );
                        }
                        break;
                }

                inner.Add( "Filled", subRequests != null?subRequests.Where(sr => sr.ConnectionState != ConnectionState.Inactive).Count():0 );
                        
                if ( partition.NextPartition != null) {
                    inner.Add( "Partitions", GetTree( partition.NextPartition, subRequests, newConcatGuid, groupTypeRoleService, scheduleService, ParentIdentifier + "_" + value, url, groupId, roleId ));
                }
                partitionList.Add( inner );
            }
            return partitionList;
        }

        #endregion

        protected void ppSignupPage_SelectItem( object sender, EventArgs e )
        {
            if ( ppSignupPage.SelectedValueAsInt().HasValue )
            {
                Settings.SignupPageGuid = PageCache.Read( ppSignupPage.SelectedValueAsInt().Value ).Guid;
            }
            SaveViewState();
        }
    }
}