﻿using Grand.Core.Domain.Common;
using Grand.Framework.Validators;
using Grand.Services.Directory;
using Grand.Services.Localization;
using Grand.Web.Models.Customer;
using Grand.Web.Validators.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Grand.Web.Validators.Customer
{
    public class CustomerAddressEditValidator : BaseGrandValidator<CustomerAddressEditModel>
    {
        public CustomerAddressEditValidator(ILocalizationService localizationService,
            IStateProvinceService stateProvinceService,
            AddressSettings addressSettings)
        {
            RuleFor(x => x.Address).SetValidator(new AddressValidator(localizationService, stateProvinceService, addressSettings));
        }
    }
}
