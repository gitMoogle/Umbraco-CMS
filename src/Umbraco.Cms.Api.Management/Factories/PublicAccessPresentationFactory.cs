﻿using Umbraco.Cms.Api.Management.ViewModels.Member.Item;
using Umbraco.Cms.Api.Management.ViewModels.MemberGroup.Item;
using Umbraco.Cms.Api.Management.ViewModels.PublicAccess;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Mapping;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Web.Common.Security;
using Umbraco.Extensions;

namespace Umbraco.Cms.Api.Management.Factories;

public class PublicAccessPresentationFactory : IPublicAccessPresentationFactory
{
    private readonly IEntityService _entityService;
    private readonly IMemberService _memberService;
    private readonly IUmbracoMapper _mapper;
    private readonly IMemberRoleManager _memberRoleManager;
    private readonly IContentService _contentService;

    public PublicAccessPresentationFactory(IEntityService entityService, IMemberService memberService, IUmbracoMapper mapper, IMemberRoleManager memberRoleManager, IContentService contentService)
    {
        _entityService = entityService;
        _memberService = memberService;
        _mapper = mapper;
        _memberRoleManager = memberRoleManager;
        _contentService = contentService;
    }

    public Task<Attempt<PublicAccessResponseModel?, PublicAccessOperationStatus>> CreatePublicAccessResponseModel(PublicAccessEntry entry)
    {
        Attempt<Guid> loginNodeKeyAttempt = _entityService.GetKey(entry.LoginNodeId, UmbracoObjectTypes.Document);
        Attempt<Guid> noAccessNodeKeyAttempt = _entityService.GetKey(entry.NoAccessNodeId, UmbracoObjectTypes.Document);

        if (loginNodeKeyAttempt.Success is false)
        {
            return Task.FromResult(Attempt.FailWithStatus<PublicAccessResponseModel?, PublicAccessOperationStatus>(PublicAccessOperationStatus.LoginNodeNotFound, null));
        }

        if (noAccessNodeKeyAttempt.Success is false)
        {
            return Task.FromResult(Attempt.FailWithStatus<PublicAccessResponseModel?, PublicAccessOperationStatus>(PublicAccessOperationStatus.ErrorNodeNotFound, null));
        }

        // unwrap the current public access setup for the client
        // - this API method is the single point of entry for both "modes" of public access (single user and role based)
        var usernames = entry.Rules
            .Where(rule => rule.RuleType == Constants.Conventions.PublicAccess.MemberUsernameRuleType)
            .Select(rule => rule.RuleValue)
            .ToArray();

        MemberItemResponseModel[] members = usernames
            .Select(username => _memberService.GetByUsername(username))
            .Select(_mapper.Map<MemberItemResponseModel>)
            .WhereNotNull()
            .ToArray();

        var allGroups = _memberRoleManager.Roles.Where(x => x.Name != null).ToDictionary(x => x.Name!);
        IEnumerable<UmbracoIdentityRole> identityRoles = entry.Rules
            .Where(rule => rule.RuleType == Constants.Conventions.PublicAccess.MemberRoleRuleType)
            .Select(rule =>
                rule.RuleValue is not null && allGroups.TryGetValue(rule.RuleValue, out UmbracoIdentityRole? memberRole)
                    ? memberRole
                    : null)
            .WhereNotNull();

        IEnumerable<IEntitySlim> groupsEntities = _entityService.GetAll(UmbracoObjectTypes.MemberGroup, identityRoles.Select(x => Convert.ToInt32(x.Id)).ToArray());
        MemberGroupItemResponseModel[] memberGroups = groupsEntities.Select(x => _mapper.Map<MemberGroupItemResponseModel>(x)!).ToArray();

        var responseModel = new PublicAccessResponseModel
        {
            Members = members,
            Groups = memberGroups,
            LoginPageId = loginNodeKeyAttempt.Result,
            ErrorPageId = noAccessNodeKeyAttempt.Result,
        };

        return Task.FromResult(Attempt.SucceedWithStatus<PublicAccessResponseModel?, PublicAccessOperationStatus>(PublicAccessOperationStatus.Success, responseModel));
    }

    public async Task<Attempt<PublicAccessEntry?, PublicAccessOperationStatus>> CreatePublicAccessEntry(PublicAccessRequestModel requestModel)
    {
        if (requestModel.MemberUserNames.Any() is false && requestModel.MemberGroupNames.Any() is false)
        {
            return Attempt.FailWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.NoAllowedEntities, null);
        }

        IContent? protectedNode = _contentService.GetById(requestModel.ContentId);

        if (protectedNode is null)
        {
            return Attempt.FailWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.ContentNotFound, null);
        }

        IContent? loginNode = _contentService.GetById(requestModel.LoginPageId);

        if (loginNode is null)
        {
            return Attempt.FailWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.LoginNodeNotFound, null);
        }

        IContent? errorNode = _contentService.GetById(requestModel.ErrorPageId);

        if (errorNode is null)
        {
            return Attempt.FailWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.ErrorNodeNotFound, null);
        }

        IEnumerable<PublicAccessRule> publicAccessRules =
            requestModel.MemberUserNames.Any() ? // We only need to check either member usernames or member group names, not both, as we have a check at the top of this method
                CreateAccessRuleList(requestModel.MemberUserNames, Constants.Conventions.PublicAccess.MemberUsernameRuleType) :
                CreateAccessRuleList(requestModel.MemberGroupNames, Constants.Conventions.PublicAccess.MemberRoleRuleType);

        var publicAccessEntry = new PublicAccessEntry(protectedNode, loginNode, errorNode, publicAccessRules);

        return await Task.FromResult(Attempt.SucceedWithStatus<PublicAccessEntry?, PublicAccessOperationStatus>(PublicAccessOperationStatus.Success, publicAccessEntry));
    }


    private IEnumerable<PublicAccessRule> CreateAccessRuleList(string[] ruleValues, string ruleType) =>
        ruleValues.Select(ruleValue => new PublicAccessRule
        {
            RuleValue = ruleValue,
            RuleType = ruleType,
        });
}
