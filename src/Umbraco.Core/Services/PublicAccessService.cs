using System.Globalization;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Extensions;

namespace Umbraco.Cms.Core.Services;

internal class PublicAccessService : RepositoryService, IPublicAccessService
{
    private readonly IPublicAccessRepository _publicAccessRepository;
    private readonly IEntityService _entityService;
    private readonly IContentService _contentService;

    public PublicAccessService(
        ICoreScopeProvider provider,
        ILoggerFactory loggerFactory,
        IEventMessagesFactory eventMessagesFactory,
        IPublicAccessRepository publicAccessRepository,
        IEntityService entityService,
        IContentService contentService)
        : base(provider, loggerFactory, eventMessagesFactory)
    {
        _publicAccessRepository = publicAccessRepository;
        _entityService = entityService;
        _contentService = contentService;
    }

    /// <summary>
    ///     Gets all defined entries and associated rules
    /// </summary>
    /// <returns></returns>
    public IEnumerable<PublicAccessEntry> GetAll()
    {
        using (ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true))
        {
            return _publicAccessRepository.GetMany();
        }
    }

    /// <summary>
    ///     Gets the entry defined for the content item's path
    /// </summary>
    /// <param name="content"></param>
    /// <returns>Returns null if no entry is found</returns>
    public PublicAccessEntry? GetEntryForContent(IContent content) =>
        GetEntryForContent(content.Path.EnsureEndsWith("," + content.Id));

    /// <summary>
    ///     Gets the entry defined for the content item based on a content path
    /// </summary>
    /// <param name="contentPath"></param>
    /// <returns>Returns null if no entry is found</returns>
    /// <remarks>
    ///     NOTE: This method get's called *very* often! This will return the results from cache
    /// </remarks>
    public PublicAccessEntry? GetEntryForContent(string contentPath)
    {
        // Get all ids in the path for the content item and ensure they all
        // parse to ints that are not -1.
        var ids = contentPath.Split(Constants.CharArrays.Comma, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) ? val : -1)
            .Where(x => x != -1)
            .ToList();

        // start with the deepest id
        ids.Reverse();

        using (ICoreScope scope = ScopeProvider.CreateCoreScope(autoComplete: true))
        {
            // This will retrieve from cache!
            var entries = _publicAccessRepository.GetMany().ToList();
            foreach (var id in ids)
            {
                PublicAccessEntry? found = entries.FirstOrDefault(x => x.ProtectedNodeId == id);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    ///     Returns true if the content has an entry for it's path
    /// </summary>
    /// <param name="content"></param>
    /// <returns></returns>
    public Attempt<PublicAccessEntry?> IsProtected(IContent content)
    {
        PublicAccessEntry? result = GetEntryForContent(content);
        return Attempt.If(result != null, result);
    }

    /// <summary>
    ///     Returns true if the content has an entry based on a content path
    /// </summary>
    /// <param name="contentPath"></param>
    /// <returns></returns>
    public Attempt<PublicAccessEntry?> IsProtected(string contentPath)
    {
        PublicAccessEntry? result = GetEntryForContent(contentPath);
        return Attempt.If(result != null, result);
    }

    /// <summary>
    ///     Adds a rule
    /// </summary>
    /// <param name="content"></param>
    /// <param name="ruleType"></param>
    /// <param name="ruleValue"></param>
    /// <returns></returns>
    public Attempt<OperationResult<OperationResultType, PublicAccessEntry>?> AddRule(IContent content, string ruleType, string ruleValue)
    {
        EventMessages evtMsgs = EventMessagesFactory.Get();
        PublicAccessEntry? entry;
        using (ICoreScope scope = ScopeProvider.CreateCoreScope())
        {
            entry = _publicAccessRepository.GetMany().FirstOrDefault(x => x.ProtectedNodeId == content.Id);
            if (entry == null)
            {
                return OperationResult.Attempt.Cannot<PublicAccessEntry>(evtMsgs); // causes rollback
            }

            PublicAccessRule? existingRule =
                entry.Rules.FirstOrDefault(x => x.RuleType == ruleType && x.RuleValue == ruleValue);
            if (existingRule == null)
            {
                entry.AddRule(ruleValue, ruleType);
            }
            else
            {
                // If they are both the same already then there's nothing to update, exit
                return OperationResult.Attempt.Succeed(evtMsgs, entry);
            }

            var savingNotifiation = new PublicAccessEntrySavingNotification(entry, evtMsgs);
            if (scope.Notifications.PublishCancelable(savingNotifiation))
            {
                scope.Complete();
                return OperationResult.Attempt.Cancel(evtMsgs, entry);
            }

            _publicAccessRepository.Save(entry);

            scope.Complete();

            scope.Notifications.Publish(
                new PublicAccessEntrySavedNotification(entry, evtMsgs).WithStateFrom(savingNotifiation));
        }

        return OperationResult.Attempt.Succeed(evtMsgs, entry);
    }

    /// <summary>
    ///     Removes a rule
    /// </summary>
    /// <param name="content"></param>
    /// <param name="ruleType"></param>
    /// <param name="ruleValue"></param>
    public Attempt<OperationResult?> RemoveRule(IContent content, string ruleType, string ruleValue)
    {
        EventMessages evtMsgs = EventMessagesFactory.Get();
        PublicAccessEntry? entry;
        using (ICoreScope scope = ScopeProvider.CreateCoreScope())
        {
            entry = _publicAccessRepository.GetMany().FirstOrDefault(x => x.ProtectedNodeId == content.Id);
            if (entry == null)
            {
                return Attempt<OperationResult?>.Fail(); // causes rollback // causes rollback
            }

            PublicAccessRule? existingRule =
                entry.Rules.FirstOrDefault(x => x.RuleType == ruleType && x.RuleValue == ruleValue);
            if (existingRule == null)
            {
                return Attempt<OperationResult?>.Fail(); // causes rollback // causes rollback
            }

            entry.RemoveRule(existingRule);

            var savingNotifiation = new PublicAccessEntrySavingNotification(entry, evtMsgs);
            if (scope.Notifications.PublishCancelable(savingNotifiation))
            {
                scope.Complete();
                return OperationResult.Attempt.Cancel(evtMsgs);
            }

            _publicAccessRepository.Save(entry);
            scope.Complete();

            scope.Notifications.Publish(
                new PublicAccessEntrySavedNotification(entry, evtMsgs).WithStateFrom(savingNotifiation));
        }

        return OperationResult.Attempt.Succeed(evtMsgs);
    }

    /// <summary>
    ///     Saves the entry
    /// </summary>
    /// <param name="entry"></param>
    public Attempt<OperationResult?> Save(PublicAccessEntry entry)
    {
        EventMessages evtMsgs = EventMessagesFactory.Get();

        using (ICoreScope scope = ScopeProvider.CreateCoreScope())
        {
            var savingNotifiation = new PublicAccessEntrySavingNotification(entry, evtMsgs);
            if (scope.Notifications.PublishCancelable(savingNotifiation))
            {
                scope.Complete();
                return OperationResult.Attempt.Cancel(evtMsgs);
            }

            _publicAccessRepository.Save(entry);
            scope.Complete();

            scope.Notifications.Publish(
                new PublicAccessEntrySavedNotification(entry, evtMsgs).WithStateFrom(savingNotifiation));
        }

        return OperationResult.Attempt.Succeed(evtMsgs);
    }

    public async Task<Attempt<PublicAccessOperationStatus>> CreateAsync(PublicAccessEntrySlim entry)
    {
        if (entry.MemberUserNames.Any() is false && entry.MemberGroupNames.Any() is false)
        {
            return Attempt.Fail(PublicAccessOperationStatus.NoAllowedEntities);
        }

        IContent? protectedNode = _contentService.GetById(entry.ContentId);

        if (protectedNode is null)
        {
            return Attempt.Fail(PublicAccessOperationStatus.ContentNotFound);
        }

        IContent? loginNode = _contentService.GetById(entry.LoginPageId);

        if (loginNode is null)
        {
            return Attempt.Fail(PublicAccessOperationStatus.LoginNodeNotFound);
        }

        IContent? errorNode = _contentService.GetById(entry.ErrorPageId);

        if (errorNode is null)
        {
            return Attempt.Fail(PublicAccessOperationStatus.ErrorNodeNotFound);
        }

        IEnumerable<PublicAccessRule> publicAccessRules =
            entry.MemberUserNames.Any() ? // We only need to check either member usernames or member group names, not both, as we have a check at the top of this method
                CreateAccessRuleList(entry.MemberUserNames, Constants.Conventions.PublicAccess.MemberUsernameRuleType) :
                CreateAccessRuleList(entry.MemberGroupNames, Constants.Conventions.PublicAccess.MemberRoleRuleType);

        var publicAccessEntry = new PublicAccessEntry(protectedNode, loginNode, errorNode, publicAccessRules);

        Attempt<OperationResult?> attempt = Save(publicAccessEntry);
        return await Task.FromResult(attempt.Success ?
            Attempt.Succeed(PublicAccessOperationStatus.Success)
            : Attempt.Fail(PublicAccessOperationStatus.CancelledByNotification));
    }

    public async Task<Attempt<PublicAccessEntry?, PublicAccessOperationStatus>> UpdateAsync(PublicAccessEntry entry)
    {
        if (entry.Rules.Any() is false)
        {
            return Attempt.FailWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.NoAllowedEntities, null);
        }

        IContent? protectedNode = _contentService.GetById(entry.ProtectedNodeId);

        if (protectedNode is null)
        {
            return Attempt.FailWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.ContentNotFound, null);
        }

        IContent? loginNode = _contentService.GetById(entry.LoginNodeId);

        if (loginNode is null)
        {
            return Attempt.FailWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.LoginNodeNotFound, null);
        }

        IContent? errorNode = _contentService.GetById(entry.NoAccessNodeId);

        if (errorNode is null)
        {
            return Attempt.FailWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.ErrorNodeNotFound, null);
        }

        Attempt<OperationResult?> attempt = Save(entry);
        return attempt.Success
            ? await Task.FromResult(Attempt.SucceedWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.Success, entry))
            : await Task.FromResult(Attempt.FailWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.CancelledByNotification, null));
    }

    /// <summary>
    ///     Deletes the entry and all associated rules
    /// </summary>
    /// <param name="entry"></param>
    public Attempt<OperationResult?> Delete(PublicAccessEntry entry)
    {
        EventMessages evtMsgs = EventMessagesFactory.Get();

        using (ICoreScope scope = ScopeProvider.CreateCoreScope())
        {
            var deletingNotification = new PublicAccessEntryDeletingNotification(entry, evtMsgs);
            if (scope.Notifications.PublishCancelable(deletingNotification))
            {
                scope.Complete();
                return OperationResult.Attempt.Cancel(evtMsgs);
            }

            _publicAccessRepository.Delete(entry);
            scope.Complete();

            scope.Notifications.Publish(
                new PublicAccessEntryDeletedNotification(entry, evtMsgs).WithStateFrom(deletingNotification));
        }

        return OperationResult.Attempt.Succeed(evtMsgs);
    }

    public Task<Attempt<PublicAccessEntry?, PublicAccessOperationStatus>> GetEntryByContentKeyAsync(Guid key)
    {
        IEntitySlim? entity = _entityService.Get(key, UmbracoObjectTypes.Document);
        if (entity is null)
        {
            return Task.FromResult(Attempt.FailWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.ContentNotFound, null));
        }

        PublicAccessEntry? entry = GetEntryForContent(entity.Path.EnsureEndsWith("," + entity.Id));

        if (entry is null)
        {
            return Task.FromResult(Attempt.SucceedWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.Success, null));
        }

        return Task.FromResult(Attempt.SucceedWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.Success, entry));
    }

    private IEnumerable<PublicAccessRule> CreateAccessRuleList(string[] ruleValues, string ruleType) =>
        ruleValues.Select(ruleValue => new PublicAccessRule
        {
            RuleValue = ruleValue,
            RuleType = ruleType,
        });
}
