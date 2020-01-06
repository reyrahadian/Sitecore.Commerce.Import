using Foundation.Import.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sitecore.Commerce.Core;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Framework.Pipelines;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Feature.Catalog.Engine
{
    public class ImportSellableItemsFromFileBlock : PipelineBlock<string, string, CommercePipelineExecutionContext>
    {
        private CommerceCommander CommerceCommander { get; set; }

        public ImportSellableItemsFromFileBlock(
            IServiceProvider serviceProvider)
        {
            CommerceCommander = serviceProvider.GetService<CommerceCommander>();
        }

        private void LogInitialization(CommercePipelineExecutionContext context, ImportSellableItemsPolicy policy)
        {
            var log = new StringBuilder();
            log.AppendLine($"{System.Environment.NewLine}");
            log.AppendLine($"{GetType().Name} - Starting");
            log.AppendLine($"Settings:");
            log.AppendLine($"\t {nameof(policy.ItemsPerBatch)} = {policy.ItemsPerBatch}");
            log.AppendLine($"\t {nameof(policy.SleepBetweenBatches)} = {policy.SleepBetweenBatches}");
            log.AppendLine($"\t {nameof(policy.FileFolderPath)}  = {policy.FileFolderPath}");
            log.AppendLine($"\t {nameof(policy.FileArchiveFolderPath)}  = {policy.FileArchiveFolderPath}");
            log.AppendLine($"\t {nameof(policy.FilePrefix)}  = {policy.FilePrefix}");
            log.AppendLine($"\t {nameof(policy.FileExtention)}  = {policy.FileExtention}");
            log.AppendLine($"\t {nameof(policy.FileRecordSeparator)}  = {policy.FileRecordSeparator}");

            context.Logger.LogInformation(log.ToString());
        }

        public override async Task<string> Run(string arg, CommercePipelineExecutionContext context)
        {
            var importPolicy = context.GetPolicy<ImportSellableItemsPolicy>();
            var sellableItemComparerByProductId = new ImportSellableItemComparer(SellableItemComparerConfiguration.ByProductId);
            var sellableItemComparerByImportData = new ImportSellableItemComparer(SellableItemComparerConfiguration.ByImportData);

            LogInitialization(context, importPolicy);

            try
            {
                var filePath = CommerceCommander.Command<GetFileCommand>().Process(context.CommerceContext, importPolicy.FileFolderPath, importPolicy.FilePrefix, importPolicy.FileExtention);
                if (string.IsNullOrEmpty(filePath))
                {
                    context.Logger.LogInformation($"{Name} - Skipping execution as there are no files to process.");
                    return null;
                }

                using (var reader = new StreamReader(filePath))
                {
                    using (var parser = new NotVisualBasic.FileIO.CsvTextFieldParser(reader))
                    {
                        parser.SetDelimiter(importPolicy.FileGroupSeparator);
                        // skip header
                        if (!parser.EndOfData) parser.ReadFields();

                        while (!parser.EndOfData)
                        {
                            var importRawLines = new List<string[]>();
                            for (int i = 0; !parser.EndOfData && i <= importPolicy.ItemsPerBatch - 1; i++)
                            {
                                importRawLines.Add(parser.ReadFields());
                            }

                            var importItems = await CommerceCommander.Command<TransformImportToSellableItemsCommand>().Process(context.CommerceContext, importRawLines);
                            var existingItems = await CommerceCommander.Command<GetSellableItemsBulkCommand>().Process(context.CommerceContext, importItems);

                            var newItems = importItems.Except(existingItems, sellableItemComparerByProductId).Select(i => new PersistEntityArgument(i)).ToList();
                            var changedItems = existingItems.Except(importItems, sellableItemComparerByImportData).ToList();

                            await CommerceCommander.Command<CopyImportToSellableItemsCommand>().Process(context.CommerceContext, importItems, changedItems);

                            var associationsToCreate = importItems.SelectMany(i => i.GetPolicy<TransientImportSellableItemDataPolicy>().ParentAssociationsToCreateList).ToList();
                            var associationsToRemove = importItems.SelectMany(i => i.GetPolicy<TransientImportSellableItemDataPolicy>().ParentAssociationsToRemoveList).ToList();

                            RemoveTransientData(importItems);

                            // Add new items
                            await CommerceCommander.Pipeline<IAddEntitiesPipeline>().Run(new PersistEntitiesArgument(newItems), context);

                            // Update existing items
                            await CommerceCommander.Pipeline<IUpdateEntitiesPipeline>().Run(new PersistEntitiesArgument(changedItems.Select(i => new PersistEntityArgument(i)).ToList()), context);

                            // NOTE: One way to improve this could be to collect a bunch of relationships for a certain number of sellable items first and submit them in bulk after a while. We are doing something similar to this during the catalog import as well.

                            await CommerceCommander.Command<AssociateToParentBulkCommand>().Process(context.CommerceContext, associationsToCreate);
                            // TODO: Need to test the disassociate, haven't had time yet
                            await CommerceCommander.Command<DisassociateToParentBulkCommand>().Process(context.CommerceContext, associationsToRemove);

                            await Task.Delay(importPolicy.SleepBetweenBatches);
                        }
                    }
                }

                CommerceCommander.Command<MoveFileCommand>().Process(context.CommerceContext, importPolicy.FileArchiveFolderPath, filePath);
            }
            catch (Exception ex)
            {
                context.Abort(await context.CommerceContext.AddMessage(
                    context.GetPolicy<KnownResultCodes>().Error,
                    Name,
                    new object[1] { ex },
                    $"{Name} Import Exception: {ex.Message}"),
                    context);
            }

            return null;
        }

        private void RemoveTransientData(IEnumerable<SellableItem> importItems)
        {
            foreach (var item in importItems)
            {
                if (item.HasPolicy<TransientImportSellableItemDataPolicy>())
                    item.RemovePolicy(typeof(TransientImportSellableItemDataPolicy));
            }
        }
    }
}
