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
using System.Linq;
using Quartz;
using Rock;
using Rock.Attribute;
using Rock.Communication;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;

namespace org.secc.Jobs
{

#pragma warning disable

    /// <summary>
    /// This job is used to convert a communication's MediumDataJson to the actual fields that were added in v7. Once all the values have been 
    /// converted, this job will delete itself.
    /// </summary>
    /// <seealso cref="Quartz.IJob" />
    [DisallowConcurrentExecution]
    [IntegerField( "How Many Records", "The number of communication records to process on each run of this job.", false, 100000, "", 0, "HowMany" )]
    [IntegerField( "Start With Record", "The record to start each run of this job.", false, 0, "", 0, "StartWith" )]
    public class MigrateCommunicationMediumDataSECC : IJob
    {
        /// <summary>
        /// Executes the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <exception cref="System.NotImplementedException"></exception>

        public void Execute( IJobExecutionContext context )
        {
            JobDataMap dataMap = context.JobDetail.JobDataMap;

            int howMany = dataMap.GetString( "HowMany" ).AsIntegerOrNull() ?? 300000;
            int startWithRecord = dataMap.GetString( "StartWith" ).AsIntegerOrNull() ?? 0;

            CommunicationSchemaUpdates();

            bool anyRemaining = UpdateCommunicationRecords( true, howMany, startWithRecord );

            if ( !anyRemaining )
            {
                // Verify that there are not any communication records with medium data.
                using ( var rockContext = new RockContext() )
                {
                    /*if ( !new CommunicationService( rockContext )
                        .Queryable()
                        .Where( c =>
                            c.MediumDataJson != null &&
                            c.MediumDataJson != "" &&
                            c.MediumDataJson != "{}" )
                        .Any() )
                    {

                        // delete job if there are no PageView or CommunicationRecipientActivity rows  left
                        var jobId = context.GetJobId();
                        var jobService = new ServiceJobService( rockContext );
                        var job = jobService.Get( jobId );
                        if ( job != null )
                        {
                            jobService.Delete( job );
                            rockContext.SaveChanges();
                            return;
                        }
                    }*/
                }
            }
        }

        /// <summary>
        /// Does any of the Communication Schema Updates that could take too long to do in the migration
        /// </summary>
        public static void CommunicationSchemaUpdates()
        {
            // (if it hasn't already) Create Indexes that weren't created in the migration
            using ( var rockContext = new RockContext() )
            {
                rockContext.Database.ExecuteSqlCommand( @"
IF NOT EXISTS(SELECT * FROM sys.indexes WHERE name = 'IX_MediumEntityTypeId' AND object_id = OBJECT_ID('CommunicationRecipient'))
BEGIN
CREATE INDEX [IX_MediumEntityTypeId] ON [dbo].[CommunicationRecipient] ([MediumEntityTypeId])
END
" );
                rockContext.Database.ExecuteSqlCommand( @"
IF NOT EXISTS(SELECT * FROM sys.indexes WHERE name = 'IX_CommunicationTemplateId' AND object_id = OBJECT_ID('Communication'))
BEGIN
CREATE INDEX [IX_CommunicationTemplateId] ON [dbo].[Communication] ([CommunicationTemplateId])
END
" );
                rockContext.Database.ExecuteSqlCommand( @"
IF NOT EXISTS(SELECT * FROM sys.indexes WHERE name = 'IX_SMSFromDefinedValueId' AND object_id = OBJECT_ID('Communication'))
BEGIN
CREATE INDEX [IX_SMSFromDefinedValueId] ON [dbo].[Communication] ([SMSFromDefinedValueId])
END
" );
                rockContext.Database.ExecuteSqlCommand( @"
IF NOT EXISTS(SELECT * FROM sys.indexes WHERE name = 'IX_SMSFromDefinedValueId' AND object_id = OBJECT_ID('CommunicationTemplate'))
BEGIN
CREATE INDEX [IX_SMSFromDefinedValueId] ON [dbo].[CommunicationTemplate] ([SMSFromDefinedValueId])
END
" );

                // (if it hasn't already) Create FK_dbo.CommunicationRecipient_dbo.EntityType_MediumEntityTypeId
                rockContext.Database.ExecuteSqlCommand( @"
IF (OBJECT_ID('[FK_dbo.CommunicationRecipient_dbo.EntityType_MediumEntityTypeId]', 'F') IS NULL)
BEGIN
	ALTER TABLE [dbo].[CommunicationRecipient] ADD CONSTRAINT [FK_dbo.CommunicationRecipient_dbo.EntityType_MediumEntityTypeId] FOREIGN KEY ([MediumEntityTypeId]) REFERENCES [dbo].[EntityType] ([Id])
END" );

                // (if it hasn't already) Create FK_dbo.Communication_dbo.CommunicationTemplate_CommunicationTemplateId]
                rockContext.Database.ExecuteSqlCommand( @"
IF (OBJECT_ID('[FK_dbo.Communication_dbo.CommunicationTemplate_CommunicationTemplateId]', 'F') IS NULL)
BEGIN
	print 'yep'
	ALTER TABLE [dbo].[Communication] ADD CONSTRAINT [FK_dbo.Communication_dbo.CommunicationTemplate_CommunicationTemplateId] FOREIGN KEY ([CommunicationTemplateId]) REFERENCES [dbo].[CommunicationTemplate] ([Id])
END
" );
            }
        }

        #region Static Methods 

        /// <summary>
        /// Migrates communication data from the MediumDataJson field to the individual fields. 
        /// </summary>
        /// <param name="updateTemplates">if set to <c>true</c> [update templates].</param>
        /// <param name="howManyToConvert">The how many to convert.</param>
        /// <returns></returns>
        public static bool UpdateCommunicationRecords( bool updateTemplates, int howManyToConvert, int startWithRecord = 0 )
        {



            bool anyRemaining = true;

            

            if ( updateTemplates )
            {
                using ( var rockContext = new RockContext() )
                {
                    var binaryFileService = new BinaryFileService( rockContext );

                    foreach ( var comm in new CommunicationTemplateService( rockContext )
                        .Queryable().Where( c =>
                            c.MediumDataJson != null &&
                            c.MediumDataJson != "" &&
                            c.MediumDataJson != "{}" ) )
                    {
                        var attachmentBinaryFileIds = new List<int>();
                        SetPropertiesFromMediumDataJson( comm, comm.MediumDataJson, attachmentBinaryFileIds );

                        foreach ( int binaryFileId in attachmentBinaryFileIds )
                        {
                            var binaryFile = binaryFileService.Get( binaryFileId );
                            if ( binaryFile != null )
                            {
                                var attachment = new CommunicationTemplateAttachment();
                                attachment.BinaryFile = binaryFile;
                                attachment.CommunicationType = CommunicationType.Email;
                                comm.AddAttachment( attachment, CommunicationType.Email );
                            }
                        }

                        comm.MediumDataJson = string.Empty;
                    }
                    rockContext.SaveChanges();
                }
            }

            int howManyLeft = howManyToConvert;
            while ( howManyLeft > 0 )
            {
                using ( var rockContext = new RockContext() )
                {
                    int take = howManyLeft < 100 ? howManyLeft : 100;
                    var query = new CommunicationService( rockContext )
                        .Queryable()
                        .OrderByDescending( c => c.Id )
                        .Skip( startWithRecord )
                        .Take( take );
                    var communications = query.ToList().Where( c =>
                            c.MediumDataJson != null &&
                            c.MediumDataJson != "" &&
                            c.MediumDataJson != "{}" ).ToList();

                    anyRemaining = communications.Count >= take;
                    howManyLeft = anyRemaining ? howManyLeft - take : 0;

                    var binaryFileService = new BinaryFileService( rockContext );

                    foreach ( var comm in communications )
                    {
                        var attachmentBinaryFileIds = new List<int>();
                        SetPropertiesFromMediumDataJson( comm, comm.MediumDataJson, attachmentBinaryFileIds );

                        foreach ( int binaryFileId in attachmentBinaryFileIds )
                        {
                            var binaryFile = binaryFileService.Get( binaryFileId );
                            if ( binaryFile != null )
                            {
                                var attachment = new CommunicationAttachment();
                                attachment.BinaryFile = binaryFile;
                                attachment.CommunicationType = CommunicationType.Email;
                                comm.AddAttachment( attachment, CommunicationType.Email );
                            }
                        }

                        comm.MediumDataJson = string.Empty;
                    }
                    rockContext.SaveChanges();
                    startWithRecord += take;
                }
            }

            return anyRemaining;
        }

        internal static void SetPropertiesFromMediumDataJson( ICommunicationDetails commDetails, string mediumDataJson, List<int> attachmentBinaryFileIds )
        {
            var mediumData = mediumDataJson.FromJsonOrNull<Dictionary<string, string>>() ?? new Dictionary<string, string>();
            if ( mediumData.Any() && commDetails.Message.IsNullOrWhiteSpace() && commDetails.SMSMessage.IsNullOrWhiteSpace() && commDetails.PushMessage.IsNullOrWhiteSpace() )
            {
                if ( mediumData.ContainsKey( "FromValue" ) )
                {
                    if ( !commDetails.SMSFromDefinedValueId.HasValue )
                    {
                        var dv = DefinedValueCache.Read( mediumData["FromValue"].AsInteger() );
                        commDetails.SMSFromDefinedValueId = dv != null ? dv.Id : (int?)null;
                    }
                    commDetails.SMSMessage = ConvertMediumData( mediumData, "Message", commDetails.SMSMessage );
                }
                else if ( mediumData.ContainsKey( "Title" ) )
                {
                    commDetails.PushTitle = ConvertMediumData( mediumData, "Title", commDetails.PushTitle );
                    commDetails.PushMessage = ConvertMediumData( mediumData, "Message", commDetails.PushMessage );
                    commDetails.PushSound = ConvertMediumData( mediumData, "Sound", commDetails.PushSound );
                }
                else
                {
                    commDetails.FromName = ConvertMediumData( mediumData, "FromName", commDetails.FromName );
                    commDetails.FromEmail = ConvertMediumData( mediumData, "FromAddress", commDetails.FromEmail );
                    commDetails.ReplyToEmail = ConvertMediumData( mediumData, "ReplyTo", commDetails.ReplyToEmail );
                    commDetails.CCEmails = ConvertMediumData( mediumData, "CC", commDetails.CCEmails );
                    commDetails.BCCEmails = ConvertMediumData( mediumData, "BCC", commDetails.BCCEmails );
                    commDetails.Message = ConvertMediumData( mediumData, "HtmlMessage", commDetails.Message );
                    attachmentBinaryFileIds = ConvertMediumData( mediumData, "Attachments", commDetails.EmailAttachmentBinaryFileIds.ToList().AsDelimited( "," ) ).SplitDelimitedValues().AsIntegerList();
                }
            }
        }

        /// <summary>
        /// Converts the medium data.
        /// </summary>
        /// <param name="mediumData">The medium data.</param>
        /// <param name="key">The key.</param>
        /// <param name="propertyValue">The property value.</param>
        /// <returns></returns>
        private static string ConvertMediumData( Dictionary<string, string> mediumData, string key, string propertyValue )
        {
            if ( propertyValue.IsNotNullOrWhitespace() )
            {
                return propertyValue;
            }

            if ( mediumData.ContainsKey( key ) )
            {
                return mediumData[key];
            }

            return string.Empty;
        }

        #endregion

    }

#pragma warning restore

}
