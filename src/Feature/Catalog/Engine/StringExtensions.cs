using Sitecore.Commerce.Core;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Framework.Conditions;

namespace Feature.Catalog.Engine
{
    public static class StringExtensions
    {
        public static string CatalogNameFromCategoryId(this string categoryId)
        {
            Condition.Requires(categoryId, nameof(categoryId)).IsNotNullOrWhiteSpace();
            string[] strArray = categoryId.RemoveIdPrefix<Category>().Split('-');
            if (strArray.Length <= 1)
                return strArray[0];
            return strArray[0];
        }
    }
}
