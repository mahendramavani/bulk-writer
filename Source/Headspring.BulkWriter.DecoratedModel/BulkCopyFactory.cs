﻿using System;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;

namespace Headspring.BulkWriter.DecoratedModel
{
    public class BulkCopyFactory : IBulkCopyFactory
    {
        private readonly SqlConnection connection;
        private readonly SqlTransaction transaction;

        public BulkCopyFactory(SqlConnection connection, SqlTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "SqlBulkCopy disposal is managed further up the return stack.")]
        public IBulkCopy Create(object item, out IPropertyToOrdinalMappings mappings)
        {
            if (null == item)
            {
                throw new ArgumentNullException("item");
            }

            Type type = item.GetType();
            var mappingsImpl = new PropertyToOrdinalMappings(type);

            SqlBulkCopyOptions bulkCopyOptions = GetBulkCopyOptions(type);
            var sqlBulkCopy = new SqlBulkCopy(this.connection, bulkCopyOptions, this.transaction)
            {
                BatchSize = 4096,
                BulkCopyTimeout = int.MaxValue,
                EnableStreaming = true
            };

            AddColumnMappings(type, sqlBulkCopy, mappingsImpl);

            mappings = mappingsImpl;

            return new WrappedBulkCopy(sqlBulkCopy);
        }

        private static SqlBulkCopyOptions GetBulkCopyOptions(Type type)
        {
            PropertyInfo[] properties = type.GetProperties();

            bool preservingIdentity = properties.Any(x =>
            {
                var mapToColumnAttribute = (MapToColumnAttribute) Attribute.GetCustomAttribute(x, typeof (MapToColumnAttribute));
                return (null != mapToColumnAttribute) && mapToColumnAttribute.InsertIdentity;
            });

            return preservingIdentity ? SqlBulkCopyOptions.KeepIdentity : SqlBulkCopyOptions.Default;
        }

        private static void AddColumnMappings(Type type, SqlBulkCopy sqlBulkCopy, PropertyToOrdinalMappings mappings)
        {
            var mapToTableAttribute = (MapToTableAttribute) Attribute.GetCustomAttribute(type, typeof (MapToTableAttribute));
            if (null == mapToTableAttribute)
            {
                throw new InvalidOperationException("The type is not decorated with the [MapToTable] attribute.");
            }

            sqlBulkCopy.DestinationTableName = mapToTableAttribute.Name;

            MapProperties(type, mappings, sqlBulkCopy);
        }

        private static void MapProperties(Type type, PropertyToOrdinalMappings mappings, SqlBulkCopy sqlBulkCopy)
        {
            PropertyInfo[] properties = type.GetProperties();
            foreach (PropertyInfo property in properties)
            {
                var mapToColumnAttribute = (MapToColumnAttribute) Attribute.GetCustomAttribute(property, typeof (MapToColumnAttribute));
                if (null != mapToColumnAttribute)
                {
                    int ordinal;
                    mappings.Add(property, out ordinal);

                    sqlBulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping
                    {
                        SourceOrdinal = ordinal,
                        DestinationOrdinal = mapToColumnAttribute.Ordinal
                    });
                }
            }
        }
    }
}