using Andgasm.Resources.Core;
using AutoMapper;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Andgasm.DynamicQuery
{
    public class DynamicQueryService
    {
        #region Fields
        ILogger _logger;
        IMapper _datamap;
        #endregion

        #region Constructors
        public DynamicQueryService(ILogger<DynamicQueryService> logger, IMapper mapper)
        {
            _logger = logger;
            _datamap = mapper;
        }
        #endregion

        #region Dynamic Report Query
        public IQueryable<T> QueryForReportOptions<T, S>(IQueryable<T> queryroot, ReportOptions options)
        {
            if (options.filter != null && options.filter.Count() > 0)
            {
                queryroot = GetQueryForFilter<T, S>(queryroot, options.filter);
            }
            if (options.sort != null && options.sort.Count() > 0)
            {
                queryroot = GetQueryForSort<T, S>(queryroot, options.sort);
            }
            return GetQueryForPageOptions(queryroot, options);
        }
        #endregion

        #region Filters
        private IQueryable<T> GetQueryForFilter<T, S>(IQueryable<T> query, FilterOptions[] filters)
        {
            foreach (var f in filters)
            {

                var mappedPropertyName = GetDestinationPropertyFor<S, T>(_datamap, f.field);
                if (mappedPropertyName != null)
                {
                    query = DynamicWhere(query, mappedPropertyName, f.value, f.@operator);
                }
                else _logger.LogWarning($"Specified filter field '{f.field}' could not be mapped to the resource. Filter for field '{f.field}' has been ignored!");
            }
            return query;
        }

        private IQueryable<T> DynamicWhere<T>(IQueryable<T> source, string columnName, object value, FilterOperator filtertype)
        {
            try
            {
                ParameterExpression table = Expression.Parameter(typeof(T), "obj");
                MemberExpression column = CompilePropertyExpression<T>(columnName, table);
                Expression valueExpression = Expression.ConvertChecked(Expression.Constant(value), column.Type); // TODO: this is a failure point due to potential garbage on request! Need to test for or catch!
                Expression where = CompileFilterFunction<T>(filtertype, table, column, valueExpression, value);
                Expression lambda = Expression.Lambda(where, new ParameterExpression[] { table });
                Type[] exprArgTypes = { source.ElementType };
                MethodCallExpression methodCall = Expression.Call(typeof(Queryable),
                                                                    "Where",
                                                                    exprArgTypes,
                                                                    source.Expression,
                                                                    lambda);
                return source.Provider.CreateQuery<T>(methodCall);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"An exception was encountered trying to perform a dynamic filter: chances are this is related to an invalid type cast!!");
                return source;
            }
        }

        private Expression CompileFilterFunction<T>(FilterOperator filtertype, ParameterExpression roottable, MemberExpression column, Expression valueExpression, object value)
        {
            Expression where = null;
            switch (filtertype.ToString())
            {
                case "neq":
                    where = Expression.NotEqual(column, valueExpression);
                    break;
                case "eq":
                    where = Expression.Equal(column, valueExpression);
                    break;
                case "lt":
                    where = Expression.LessThan(column, valueExpression);
                    break;
                case "lte":
                    where = Expression.LessThanOrEqual(column, valueExpression);
                    break;
                case "gt":
                    where = Expression.GreaterThan(column, valueExpression);
                    break;
                case "gte":
                    where = Expression.GreaterThanOrEqual(column, valueExpression);
                    break;
                case "contains":
                    where = CompileExpressionFunction<T>(roottable, column, "Contains", value.ToString()).Body;
                    break;
                case "startswith":
                    where = CompileExpressionFunction<T>(roottable, column, "StartsWith", value.ToString()).Body;
                    break;
                case "endswith":
                    where = CompileExpressionFunction<T>(roottable, column, "EndsWith", value.ToString()).Body;
                    break;
                default:
                    _logger.LogWarning($"Specified filter function '{filtertype}' is not supported by the dynamic expression service. Filter for field '{column.Member.Name}' has been ignored!");
                    break;
            }
            return where;
        }
        #endregion

        #region Sorts
        private IQueryable<T> GetQueryForSort<T, S>(IQueryable<T> query, SortOptions[] sorts)
        {
            foreach (var s in sorts)
            {
                var mappedPropertyName = GetDestinationPropertyFor<S, T>(_datamap, s.field);
                if (mappedPropertyName != null)
                {
                    query = DynamicOrder(query, mappedPropertyName, s.dir);
                }
                else _logger.LogWarning($"Specified sort field '{s.field}' could not be mapped to the resource. Sort for field '{s.field}' has been ignored!");
            }
            return query;
        }

        private IQueryable<T> DynamicOrder<T>(IQueryable<T> source, string columnName, SortDirection filtertype)
        {
            try
            {
                ParameterExpression table = Expression.Parameter(typeof(T), "obj");
                MemberExpression column = CompilePropertyExpression<T>(columnName, table);
                MethodInfo method = typeof(Queryable).GetMethods().Single(m => m.Name == CompileSortFunction(filtertype) &&
                                                                               m.GetParameters().Length == 2);
                MethodInfo concreteMethod = method.MakeGenericMethod(typeof(T), column.Type);
                Expression orderBy = Expression.Lambda(column, table);
                return (IQueryable<T>)concreteMethod.Invoke(null, new object[] { source, orderBy });
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"An exception was encountered trying to perform a dynamic sort: chances are this is related to an invalid type cast!!");
                return source;
            }
        }

        private string CompileSortFunction(SortDirection sortdir)
        {
            return (sortdir == SortDirection.asc ? "OrderBy" : "OrderByDescending");
        }
        #endregion

        #region Paging
        private IQueryable<T> GetQueryForPageOptions<T>(IQueryable<T> query, ReportOptions options)
        {
            return query.Skip(options.skip).Take(options.take);
        }
        #endregion

        #region Expression Helpers
        private Expression<Func<T, bool>> CompileExpressionFunction<T>(ParameterExpression parameterExp, MemberExpression propertyExp, string funcname, string value)
        {
            MethodInfo method = typeof(string).GetMethod(funcname, new[] { typeof(string) });
            var someValue = Expression.Constant(value, typeof(string));
            var containsMethodExp = Expression.Call(propertyExp, method, someValue);
            return BinaryExpression.Lambda<Func<T, bool>>(containsMethodExp, parameterExp);
        }

        private MemberExpression CompilePropertyExpression<T>(string propertypath, ParameterExpression roottable)
        {
            string[] columns = propertypath.Split('.');
            var property = typeof(T).GetProperty(columns[0]);
            var propertyAccess = Expression.MakeMemberAccess(roottable, property);
            if (columns.Length > 1)
            {
                for (int i = 1; i < columns.Length; i++)
                {
                    propertyAccess = Expression.MakeMemberAccess(propertyAccess, propertyAccess.Type.GetProperty(columns[i]));
                }
            }
            return propertyAccess;
        }

        private static string GetDestinationPropertyFor<TSrc, TDst>(IMapper mapper, string sourceProperty)
        {
            var mappedproperty = typeof(TSrc).GetProperties().FirstOrDefault(x => x.Name.ToLowerInvariant() == sourceProperty.ToLowerInvariant());
            if (mappedproperty != null)
            {
                var mappedname = mappedproperty.Name;
                var map = mapper.ConfigurationProvider.FindTypeMapFor<TDst, TSrc>();
                var propertyMap = map.PropertyMaps.FirstOrDefault(pm => pm.DestinationMember.Name == mappedname);
                var filterattribute = propertyMap.DestinationMember.CustomAttributes.FirstOrDefault(x => x.AttributeType == typeof(FilterAttribute));
                if (filterattribute != null)
                {
                    var att = filterattribute.NamedArguments.FirstOrDefault(x => x.MemberName == "AlternativeColumntoFilter");
                    if (att != null)
                    {
                        var ac = att.TypedValue.Value.ToString();
                        return GetDestinationPropertyFor<TSrc, TDst>(mapper, ac);
                    }
                }
                else if (propertyMap != null)
                {
                    return propertyMap.CustomMapExpression.Body.ToString().Replace("y.", "");
                }
            }
            return null;
        }
        #endregion
    }
}
