﻿using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Grand.Web.Services;
using System.Linq;
using Grand.Web.Models.PrivateMessages;
using Grand.Web.Models.Common;
using Grand.Core.Domain.Forums;
using Grand.Services.Forums;
using Grand.Core;
using System.Collections.Generic;
using Grand.Services.Customers;
using Grand.Core.Domain.Customers;
using Grand.Services.Helpers;

namespace Grand.Web.ViewComponents
{
    public class PrivateMessagesInboxViewComponent : ViewComponent
    {
        private readonly ForumSettings _forumSettings;
        private readonly IForumService _forumService;
        private readonly IWorkContext _workContext;
        private readonly IStoreContext _storeContext;
        private readonly ICustomerService _customerService;
        private readonly CustomerSettings _customerSettings;
        private readonly IDateTimeHelper _dateTimeHelper;
        public PrivateMessagesInboxViewComponent(ForumSettings forumSettings, IForumService forumService,
            IWorkContext workContext, IStoreContext storeContext, ICustomerService customerService,
            CustomerSettings customerSettings, IDateTimeHelper dateTimeHelper)
        {
            this._forumSettings = forumSettings;
            this._forumService = forumService;
            this._workContext = workContext;
            this._storeContext = storeContext;
            this._customerService = customerService;
            this._customerSettings = customerSettings;
            this._dateTimeHelper = dateTimeHelper;
        }

        public IViewComponentResult Invoke(int page, string tab)
        {
            if (page > 0)
            {
                page -= 1;
            }

            var pageSize = _forumSettings.PrivateMessagesPageSize;

            var list = _forumService.GetAllPrivateMessages(_storeContext.CurrentStore.Id,
                "", _workContext.CurrentCustomer.Id, null, null, false, string.Empty, page, pageSize);

            var inbox = new List<PrivateMessageModel>();

            foreach (var pm in list)
            {
                var fromCustomer = _customerService.GetCustomerById(pm.FromCustomerId);
                var toCustomer = _customerService.GetCustomerById(pm.ToCustomerId);
                inbox.Add(new PrivateMessageModel
                {
                    Id = pm.Id,
                    FromCustomerId = fromCustomer.Id,
                    CustomerFromName = fromCustomer.FormatUserName(),
                    AllowViewingFromProfile = _customerSettings.AllowViewingProfiles && fromCustomer != null && !fromCustomer.IsGuest(),
                    ToCustomerId = toCustomer.Id,
                    CustomerToName = toCustomer.FormatUserName(),
                    AllowViewingToProfile = _customerSettings.AllowViewingProfiles && toCustomer != null && !toCustomer.IsGuest(),
                    Subject = pm.Subject,
                    Message = pm.Text,
                    CreatedOn = _dateTimeHelper.ConvertToUserTime(pm.CreatedOnUtc, DateTimeKind.Utc),
                    IsRead = pm.IsRead,
                });
            }

            var pagerModel = new PagerModel
            {
                PageSize = list.PageSize,
                TotalRecords = list.TotalCount,
                PageIndex = list.PageIndex,
                ShowTotalSummary = false,
                RouteActionName = "PrivateMessagesPaged",
                UseRouteLinks = true,
                RouteValues = new PrivateMessageRouteValues { page = page, tab = tab }
            };

            var model = new PrivateMessageListModel
            {
                Messages = inbox,
                PagerModel = pagerModel
            };

            return View(model);

        }
    }
}