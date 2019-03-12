using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Andgasm.DynamicQuery
{
    public enum FilterOperator
    {
        neq,
        eq,
        lt,
        lte,
        gt,
        gte,
        contains,
        startswith,
        endswith
    }

    public enum SortDirection
    {
        asc,
        desc
    }

    [TypeConverter(typeof(ReportOptionsConverter))]
    public class ReportOptions
    {
        public int skip { get; set; }
        public int take { get; set; }
        public FilterOptions[] filter { get; set; }
        public SortOptions[] sort { get; set; }
    }

    public class FilterOptions
    {
        public string field { get; set; }
        public FilterOperator @operator { get; set; }
        public object value { get; set; }
    }

    public class SortOptions
    {
        public string field { get; set; }
        public SortDirection dir { get; set; }
    }

    public class FilterConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return true;
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            var pval = ((string)value);//.Replace("[", "").Replace("]", "");
            return JsonConvert.DeserializeObject<FilterOptions[]>(pval, new JsonSerializerSettings
            {
                ContractResolver = new NoTypeConverterContractResolver<FilterOptions[]>(),
            });
        }
    }

    public class ReportOptionsConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return true;
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            var pval = ((string)value);
            return JsonConvert.DeserializeObject<ReportOptions>(pval, new JsonSerializerSettings
            {
                ContractResolver = new NoTypeConverterContractResolver<ReportOptions>(),
            });
        }
    }

    class NoTypeConverterContractResolver<T> : DefaultContractResolver
    {
        protected override JsonContract CreateContract(Type objectType)
        {
            if (typeof(T).IsAssignableFrom(objectType))
            {
                var contract = this.CreateObjectContract(objectType);
                contract.Converter = null; 
                return contract;
            }
            return base.CreateContract(objectType);
        }
    }
}
