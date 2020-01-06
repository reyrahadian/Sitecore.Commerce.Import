using Microsoft.Extensions.Logging;
using Sitecore.Commerce.Core;
using Sitecore.Commerce.Core.Commands;
using Sitecore.Commerce.Plugin.Catalog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Foundation.Import.Engine
{
    public class AssociateToParentBulkCommand : CommerceCommand
    {
        public AssociateToParentBulkCommand(IServiceProvider serviceProvider) : base(serviceProvider) { }

        public async Task<bool> Process(CommerceContext commerceContext, IEnumerable<ParentAssociationModel> associationList)
        {
            using (CommandActivity.Start(commerceContext, this))
            {
                commerceContext.Logger.LogInformation($"Called - {nameof(AssociateToParentBulkCommand)}.");

                // TODO:
                // Depending on the amount of items in the associationList it might make sense to add some additional batching.
                // When dealing with multiple batches, the context needs to be recreated to avoid transactional errors and performance issues due to too many messages.

                // Need to clear message as any prior error will cause all transactions to abort.
                commerceContext = new CommerceContext(commerceContext.Logger, commerceContext.TelemetryClient)
                {
                    GlobalEnvironment = commerceContext.GlobalEnvironment,
                    Environment = commerceContext.Environment,
                    Headers = commerceContext.Headers
                };

                var listsEntitiesArgument = new ListsEntitiesArgument();
                foreach (var association in associationList)
                {
                    var relationshipType = Command<GetRelationshipTypeCommand>().Process(commerceContext, association.ParentId, association.ItemId);
                    var listName = $"{relationshipType}-{association.ParentId.SimplifyEntityName()}";

                    listsEntitiesArgument.ListNamesAndEntityIds.TryAdd(listName, new List<string> { association.ItemId });
                }

                await Pipeline<IAddListsEntitiesPipeline>().Run(listsEntitiesArgument, commerceContext.PipelineContextOptions);

                commerceContext.Logger.LogInformation($"Completed - {nameof(AssociateToParentBulkCommand)}.");

                return true;
            }
        }
    }
}
