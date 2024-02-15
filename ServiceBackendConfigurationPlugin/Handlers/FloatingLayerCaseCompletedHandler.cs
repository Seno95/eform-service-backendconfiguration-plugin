using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using Rebus.Handlers;
using SendGrid.Helpers.Mail;
using ServiceBackendConfigurationPlugin.Infrastructure.Helpers;
using ServiceBackendConfigurationPlugin.Messages;
using ServiceBackendConfigurationPlugin.Resources;

namespace ServiceBackendConfigurationPlugin.Handlers;

public class FloatingLayerCaseCompletedHandler : IHandleMessages<FloatingLayerCaseCompleted>
{
    private readonly eFormCore.Core _sdkCore;
    private readonly ItemsPlanningDbContextHelper _itemsPlanningDbContextHelper;
    private readonly BackendConfigurationDbContextHelper _backendConfigurationDbContextHelper;

    public FloatingLayerCaseCompletedHandler(eFormCore.Core sdkCore, ItemsPlanningDbContextHelper itemsPlanningDbContextHelper, BackendConfigurationDbContextHelper backendConfigurationDbContextHelper)
    {
        _sdkCore = sdkCore;
        _itemsPlanningDbContextHelper = itemsPlanningDbContextHelper;
        _backendConfigurationDbContextHelper = backendConfigurationDbContextHelper;
    }

    public async Task Handle(FloatingLayerCaseCompleted message)
    {
        await using var sdkDbContext = _sdkCore.DbContextHelper.GetDbContext();
        await using var
            itemsPlanningPnDbContext = _itemsPlanningDbContextHelper.GetDbContext();
        await using var backendConfigurationPnDbContext =
            _backendConfigurationDbContextHelper.GetDbContext();

        var eformQuery = sdkDbContext.CheckLists
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .AsQueryable();

        var eformIdForControlFloatingLayer = await eformQuery
            .Where(x => x.OriginalId == "142142new2")
            .Select(x => x.Id)
            .FirstOrDefaultAsync();

        var dbCase = await sdkDbContext.Cases
                         .AsNoTracking()
                         .FirstOrDefaultAsync(x => x.Id == message.CaseId) ??
                     await sdkDbContext.Cases
                         .FirstOrDefaultAsync(x => x.MicrotingCheckUid == message.CheckId);

        var fieldValues = await sdkDbContext.FieldValues
            .Where(x => x.CaseId == dbCase.Id)
            .Include(x => x.Field)
            .ThenInclude(x => x.FieldType)
            .ToListAsync();

        var checkBoxFloatingLayerOk = fieldValues
            .Where(x => x.Field.FieldType.Type == Constants.FieldTypes.CheckBox)
            .Select(x =>
                !string.IsNullOrEmpty(x.Value) &&
                x.Value ==
                "checked") // string.IsNullOrEmpty(x.Value) ? false : x.Value == "checked" ? true : false
            .First();

        var statusOrActivityFieldIdAndKey = fieldValues
            .Where(x => x.Field.FieldType.Type == Constants.FieldTypes.SingleSelect)
            .Select(x => new {Key = x.Value, x.FieldId})
            .First();

        var statusOrActivity = string.IsNullOrEmpty(statusOrActivityFieldIdAndKey.Key)
            ? ""
            : await sdkDbContext.FieldOptions
                .Where(x => x.Key == statusOrActivityFieldIdAndKey.Key)
                .Where(x => x.FieldId == (int) statusOrActivityFieldIdAndKey.FieldId)
                .Include(x => x.FieldOptionTranslations)
                .SelectMany(x => x.FieldOptionTranslations)
                .Where(x => x.LanguageId == 1) // get only danish
                .Select(x => x.Text)
                .FirstOrDefaultAsync();

        var listWithStatuses = new List<string>
        {
            "Beholder omrørt", // da
            // "Slurry tank stirred", // en
            "Gylle udbragt", // da
            // "Slurry delivered", // en
            "Halm tilført", // da
            // "Straw added", // en
            "Flyttet til anden beholder", // da
            // "Moved to another slurry tank", // en
            "Modtaget biogas-gylle", // da
            // "Biogas slurry received", // en
            "" // Blank
        };
        if (checkBoxFloatingLayerOk == false && listWithStatuses.Contains(statusOrActivity))
        {
            // retract old eform
            // await _sdkCore.CaseDelete(dbCase.Id);
            // deploy new eform with old data, reminder: Current data + 6 days
            // planningCaseSite.MicrotingSdkSiteId

            var oldComment = fieldValues
                .Where(x => x.Field.FieldType.Type == Constants.FieldTypes.Comment)
                .Select(x => x.Value)
                .First();

            // get name tank from linked planning
            var itemPlanningId = await itemsPlanningPnDbContext.PlanningCaseSites
                .Where(x => x.MicrotingSdkCaseId == dbCase.Id)
                .Select(x => x.PlanningId)
                .FirstOrDefaultAsync();

            // var itemPlanning = await itemsPlanningPnDbContext.Plannings.FirstAsync(x => x.Id == itemPlanningId);
            var itemPlanningSites = await itemsPlanningPnDbContext.PlanningSites
                .Where(x => x.PlanningId == itemPlanningId).ToListAsync();

            var planningCaseSite =
                await itemsPlanningPnDbContext.PlanningCaseSites.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.MicrotingSdkCaseId == dbCase.Id);

            if (planningCaseSite == null)
            {
                // var site = await sdkDbContext.Sites.FirstOrDefaultAsync(x => x.MicrotingUid == caseDto.SiteUId);
                var checkListSite = await sdkDbContext.CheckListSites.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.MicrotingUid == message.MicrotingUId).ConfigureAwait(false);
                if (checkListSite == null)
                {
                    return;
                }
                planningCaseSite =
                    await itemsPlanningPnDbContext.PlanningCaseSites.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.MicrotingCheckListSitId == checkListSite.Id).ConfigureAwait(false);
            }

            if (planningCaseSite == null)
            {
                Console.WriteLine($"planningCaseSite is null for CheckId: {message.CheckId}");
                return;
            }

            var planning =
                await itemsPlanningPnDbContext.Plannings.AsNoTracking()
                    .FirstAsync(x => x.Id == planningCaseSite.PlanningId);

            PlanningCase planningCase = new PlanningCase()
            {
                PlanningId = planning.Id,
                Status = 66,
                MicrotingSdkeFormId = (int) dbCase.CheckListId
            };
            foreach (var itemPlanningSite in itemPlanningSites)
            {
                var site = await sdkDbContext.Sites.FirstAsync(
                    x => x.Id == itemPlanningSite.SiteId);
                var siteLanguage =
                    await sdkDbContext.Languages.FirstAsync(x => x.Id == site.LanguageId);
                var nameTank = await _itemsPlanningDbContextHelper
                    .GetDbContext().PlanningNameTranslation
                    .Where(x => x.PlanningId == itemPlanningId)
                    .Where(x => x.LanguageId == site.LanguageId)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync();

                var mainElement =
                    await _sdkCore.ReadeForm(eformIdForControlFloatingLayer, siteLanguage);
                ((DataElement) mainElement.ElementList[0]).DataItemGroupList[0].DataItemList[0]
                    .Description.InderValue =
                    $"<strong>{Translations.FollowUpFloatingLayerCheck}</strong><br>" +
                    $"<strong>{Translations.SlurryTank}:</strong> {nameTank}<br>" +
                    $"<strong>{Translations.LastUpdated}:</strong> {dbCase.DoneAt.Value:dd-MM-yyyy}<br>" +
                    $"<strong>{Translations.StatusOrActivity}:</strong>{statusOrActivity}<br>" +
                    $"<strong>{Translations.ControlLatest}:</strong> {dbCase.DoneAt.Value.AddDays(6):dd-MM-yyyy}";
                ((Comment) ((DataElement) mainElement.ElementList[0]).DataItemList[4]).Value =
                    oldComment;

                //mainElement.StartDate = DateTime.Now.AddDays(6).ToUniversalTime();
                mainElement.StartDate = DateTime.Now.AddDays(1).ToUniversalTime();
                mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                mainElement.CheckListFolderName = await sdkDbContext.Folders
                    .Where(x => x.Id == planning.SdkFolderId)
                    .Select(x => x.MicrotingUid.ToString())
                    .FirstAsync();
                planningCaseSite = new PlanningCaseSite
                {
                    MicrotingSdkSiteId = site.Id,
                    MicrotingSdkeFormId = (int) dbCase.CheckListId,
                    Status = 66,
                    PlanningId = planning.Id,
                    PlanningCaseId = planningCase.Id
                };

                await planningCaseSite.Create(itemsPlanningPnDbContext);
                var folder = await GetTopFolderName((int) planning.SdkFolderId, sdkDbContext);
                string body = "";
                if (folder != null)
                {
                    // planning.SdkFolderId = sdkDbContext.Folders
                        // .FirstOrDefault(y => y.Id == planning.SdkFolderId)
                        // ?.Id;
                    FolderTranslation folderTranslation =
                        await sdkDbContext.FolderTranslations.FirstAsync(x =>
                            x.FolderId == folder.Id && x.LanguageId == site.LanguageId);
                    body = $"{folderTranslation.Name} ({site.Name};{mainElement.StartDate:dd.MM.yyyy})";
                }

                PlanningNameTranslation planningNameTranslation =
                    await itemsPlanningPnDbContext.PlanningNameTranslation.FirstAsync(x =>
                        x.PlanningId == planning.Id
                        && x.LanguageId == site.LanguageId);

                //mainElement.PushMessageBody = body;
                mainElement.PushMessageBody = "";
                //mainElement.PushMessageTitle = planningNameTranslation.Name;
                mainElement.PushMessageTitle = "";
                // var _ = await _sdkCore.CaseCreate(mainElement, "", (int)site.MicrotingUid, dbCase.FolderId);
                var caseId = await _sdkCore.CaseCreate(mainElement, "", (int) site.MicrotingUid,
                    planning.SdkFolderId);

                if (caseId != null)
                {
                    var caseDto = await _sdkCore.CaseLookupMUId((int) caseId);
                    if (caseDto?.CaseId != null)
                        planningCaseSite.MicrotingSdkCaseId = (int) caseDto.CaseId;
                    await planningCaseSite.Update(itemsPlanningPnDbContext);
                }
            }

            try {

                var backendPlanning = await backendConfigurationPnDbContext.AreaRulePlannings.AsNoTracking()
                    .Include(x => x.AreaRule.AreaRuleTranslations)
                    .Where(x => x.ItemPlanningId == planningCaseSite.PlanningId).FirstAsync();

                var property =
                    await backendConfigurationPnDbContext.Properties.FirstAsync(x =>
                        x.Id == backendPlanning.PropertyId);
                // var sendGridKey =
                //     _baseDbContext.ConfigurationValues.Single(x => x.Id == "EmailSettings:SendGridKey");

                // var fromEmailAddress = new EmailAddress("no-reply@microting.com",
                    // $"KemiKontrol for : {property.Name}");
                var toEmailAddress = new List<EmailAddress>();
                if (!string.IsNullOrEmpty(property.MainMailAddress))
                {
                    toEmailAddress.AddRange(property.MainMailAddress.Split(";").Select(s => new EmailAddress(s)));
                }

                if (toEmailAddress.Count > 0)
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    var assemblyName = assembly.GetName().Name;

                    var stream =
                        assembly.GetManifestResourceStream($"{assemblyName}.Resources.Flydelagskontrol_1.0_Libre.html");
                    string html;
                    if (stream == null)
                    {
                        throw new InvalidOperationException("Resource not found");
                    }

                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        html = await reader.ReadToEndAsync();
                    }
                    var newHtml = html;
                    newHtml = newHtml.Replace("{{propertyName}}", property.Name);
                    newHtml = newHtml.Replace("{{todaysDate}}", DateTime.Now.ToString("dd-MM-yyyy"));
                    newHtml = newHtml.Replace("{{emailaddresses}}", property.MainMailAddress);
                    newHtml = newHtml.Replace("{{slurryTankName}}", backendPlanning.AreaRule.AreaRuleTranslations.First(x => x.LanguageId == 1).Name);
                    newHtml = newHtml.Replace("{{slurryTankStatus}}", statusOrActivity);
                    newHtml = newHtml.Replace("{{comment}}", oldComment);

                    var sdkContext =  _sdkCore.DbContextHelper.GetDbContext();
                    var folder = await sdkContext.Folders.FirstAsync(x => x.Id == planning.SdkFolderId);

                    FolderTranslation folderTranslation =
                        await sdkDbContext.FolderTranslations.FirstAsync(x =>
                            x.FolderId == folder.ParentId && x.LanguageId == 1);
                    newHtml = newHtml.Replace("{{slurryTankFolderName}}", folderTranslation.Name);

                    PlanningNameTranslation planningNameTranslation =
                        await itemsPlanningPnDbContext.PlanningNameTranslation.FirstAsync(x =>
                            x.PlanningId == planning.Id
                            && x.LanguageId == 1);

                    var backendConfigurationDbContext = _backendConfigurationDbContextHelper.GetDbContext();
                    var email = new Email
                    {
                        Body = newHtml,
                        Subject = $"Opfølgning flydelag: {planningNameTranslation.Name}; {property.Name}",
                        To = property.MainMailAddress,
                        DelayedUntil = DateTime.Now.AddDays(6).ToUniversalTime(),
                        From = "no-reply@microting.com",
                        Status = "not-sent"
                    };
                    await email.Create(backendConfigurationDbContext).ConfigureAwait(false);

                    var emailAttachment = new EmailAttachment
                    {
                        EmailId = email.Id,
                        CidName = "eform-logo",
                        ResourceName = "KemiKontrol_rapport_1.0_Libre_html_5d7c0d01f9da8102.png"
                    };
                    await emailAttachment.Create(backendConfigurationDbContext).ConfigureAwait(false);
                }
            } catch (Exception e) {
                Console.WriteLine(e.Message);
            }
        }

    }


    private async Task<Folder> GetTopFolderName(int folderId, MicrotingDbContext dbContext)
    {
        var result = await dbContext.Folders.FirstOrDefaultAsync(y => y.Id == folderId);
        if (result is {ParentId: { }})
        {
            result = await GetTopFolderName((int)result.ParentId, dbContext);
        }
        return result;
    }
}