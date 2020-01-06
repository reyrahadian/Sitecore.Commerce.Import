using Sitecore.Commerce.Core;
using Sitecore.Commerce.Core.Commands;
using Sitecore.Commerce.Plugin.Catalog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Feature.Catalog.Engine
{
    public class GetSellableItemsBulkCommand : CommerceCommand
    {
        public GetSellableItemsBulkCommand(IServiceProvider serviceProvider) : base(serviceProvider) { }

        public async Task<IEnumerable<SellableItem>> Process(CommerceContext commerceContext, IEnumerable<SellableItem> items)
        {
            using (CommandActivity.Start(commerceContext, this))
            {
                var findEntitiesArgument = new FindEntitiesArgument(items.Select(i => new FindEntityArgument(typeof(SellableItem), i.Id)).ToList(), typeof(SellableItem));

                return (await Pipeline<IFindEntitiesPipeline>().Run(findEntitiesArgument, commerceContext.PipelineContextOptions)).OfType<SellableItem>();
            }
        }
    }
}
