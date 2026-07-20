using System;

namespace REFrameXIV.Services;


public static class StatusDisplayService
{
    public static bool TryResolve(uint statusId, uint statusParameter, out StatusDisplayData data)
    {
        data = default;

        try
        {
            var statusSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
            if (!statusSheet.TryGetRow(statusId, out var statusRow))
                return false;

            var iconId = statusRow.Icon;
            var name = statusRow.Name.ToString().Trim();
            var description = statusRow.Description.ToString().Trim();
            var isFreeCompanyAction = statusRow.IsFcBuff;


            if (isFreeCompanyAction && statusParameter != 0)
            {
                try
                {
                    var companyActionSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.CompanyAction>();
                    if (companyActionSheet.TryGetRow(statusParameter, out var companyAction))
                    {
                        var companyActionName = companyAction.Name.ToString().Trim();
                        var companyActionDescription = companyAction.Description.ToString().Trim();

                        if (!string.IsNullOrWhiteSpace(companyActionName))
                            name = companyActionName;
                        if (!string.IsNullOrWhiteSpace(companyActionDescription))
                            description = companyActionDescription;
                        if (companyAction.Icon > 0)
                            iconId = (uint)companyAction.Icon;
                    }
                }
                catch (Exception ex)
                {


                    Plugin.Log.Verbose(
                        ex,
                        "RE:Frame could not resolve CompanyAction {CompanyActionId} for status {StatusId}.",
                        statusParameter,
                        statusId);
                }
            }

            if (iconId == 0)
                return false;
            if (string.IsNullOrWhiteSpace(name))
                name = "Status Effect";

            data = new StatusDisplayData(
                statusId,
                statusParameter,
                iconId,
                name,
                description,
                statusRow.StatusCategory == 2,
                isFreeCompanyAction);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(
                ex,
                "RE:Frame skipped status display data {StatusId} ({StatusParameter}).",
                statusId,
                statusParameter);
            return false;
        }
    }
}

public readonly record struct StatusDisplayData(
    uint StatusId,
    uint StatusParameter,
    uint IconId,
    string Name,
    string Description,
    bool IsDebuff,
    bool IsFreeCompanyAction);
