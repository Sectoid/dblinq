////////////////////////////////////////////////////////////////////
// MIT license:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
//
// Authors:
//        Jiri George Moudry
////////////////////////////////////////////////////////////////////

using System;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Linq.Expressions;
using System.Data.Linq;
using System.Data.Linq.Mapping;
using DBLinq.Linq;
using DBLinq.Linq.Mapping;

namespace DBLinq.util
{
    /// <summary>
    /// Helper class which does the walking over Types to analyze attributes
    /// </summary>
    public class AttribHelper
    {
        static Dictionary<AssociationAttribute,PropertyInfo> s_knownAssocs = new Dictionary<AssociationAttribute,PropertyInfo>();

        /// <summary>
        /// given type Products, find it's [TableAttribute] (or null)
        /// </summary>
        public static TableAttribute GetTableAttrib(Type t)
        {
            object[] objs = t.GetCustomAttributes(typeof(TableAttribute),false);
            TableAttribute tbl = objs.OfType<TableAttribute>().FirstOrDefault();
            return tbl;
        }

        /// <summary>
        /// from class Employee, walk all properties, extract their [Column] attribs
        /// </summary>
        public static T[] FindPropertiesWithGivenAttrib<T>(Type t) where T:Attribute
        {
            List<T> lst = new List<T>();
            PropertyInfo[] infos = t.GetProperties();
            foreach(PropertyInfo pi in infos)
            {
                object[] objs = pi.GetCustomAttributes(typeof(T),false);
                List<T> partList = objs.OfType<T>().ToList();
                lst.AddRange(partList);
            }
            return lst.ToArray();
        }

        /// <summary>
        /// from class Employee, walk all properties with [Column] attribs, 
        /// and return array of {field,[Column]} pairs.
        /// </summary>
        public static KeyValuePair<PropertyInfo,T>[] FindPropertiesWithGivenAttrib2<T>(Type t) where T : Attribute
        {
            List<KeyValuePair<PropertyInfo, T>> lst = new List<KeyValuePair<PropertyInfo, T>>();
            PropertyInfo[] infos = t.GetProperties();
            foreach (PropertyInfo pi in infos)
            {
                object[] objs = pi.GetCustomAttributes(typeof(T), false);
                List<T> partList = objs.OfType<T>().ToList();
                //lst.AddRange(partList);
                if (partList.Count == 0)
                    continue;
                lst.Add(new KeyValuePair<PropertyInfo,T>(pi,partList[0]));
            }
            return lst.ToArray();
        }

        /// <summary>
        /// from class Employee, walk all properties, extract their [Column] attribs
        /// </summary>
        public static ColumnAttribute[] GetColumnAttribs(Type t)
        {
            return FindPropertiesWithGivenAttrib<ColumnAttribute>(t);
        }

        /// <summary>
        /// for Microsoft SQL bulk insert, we need a version of GetColumnAttribs with their PropInfo
        /// </summary>
        public static KeyValuePair<PropertyInfo, ColumnAttribute>[] GetColumnAttribs2(Type t)
        {
            return FindPropertiesWithGivenAttrib2<ColumnAttribute>(t);
        }

        /// <summary>
        /// from one column Employee.EmpID, extract its [Column] attrib
        /// </summary>
        public static ColumnAttribute GetColumnAttrib(MemberInfo memberInfo)
        {
            object[] colAttribs = memberInfo.GetCustomAttributes(typeof(ColumnAttribute), false);
            if (colAttribs.Length != 1)
            {
                return null;
            }
            ColumnAttribute colAtt0 = colAttribs[0] as ColumnAttribute;
            return colAtt0;
        }

        /// <summary>
        /// get name of column in SQL table for one C# field.
        /// </summary>
        public static string GetSQLColumnName(MemberInfo memberInfo)
        {
            ColumnAttribute colAtt0 = GetColumnAttrib(memberInfo);
            return colAtt0 == null ? null : colAtt0.Name;
        }

        /// <summary>
        /// prepate ProjectionData - which holds ctor and field accessors
        /// </summary>
        public static ProjectionData GetProjectionData(Type t)
        {
            ProjectionData projData = new ProjectionData();
            projData.type = t;
            projData.ctor = t.GetConstructor(new Type[0]);
            ConstructorInfo[] ctors = t.GetConstructors();
            if(ctors.Length==2 && ctors[0]==projData.ctor)
            {
                //classes generated by MysqlMetal have ctor2 with multiple params:
                projData.ctor2 = ctors[1]; 
            }
            if (projData.ctor2 == null && ctors.Length == 1 && ctors[0].GetParameters().Length > 0)
            {
                //Beta2: "<>f__AnonymousType" ctor has >0 params
                projData.ctor2 = ctors[0];
            }

            projData.tableAttribute = GetTableAttrib(t);
            //List<ColumnAttribute> lst = new List<ColumnAttribute>();
            PropertyInfo[] props = t.GetProperties();
            foreach(PropertyInfo prop in props)
            {
                object[] objs = prop.GetCustomAttributes(typeof(ColumnAttribute),false);
                List<ColumnAttribute> colAtt = objs.OfType<ColumnAttribute>().ToList();
                if(colAtt.Count==0)
                    continue; //not a DB field
                ProjectionData.ProjectionField projField = new ProjectionData.ProjectionField(prop);
                //projField.type = prop.PropertyType;
                //projField.propInfo = prop;
                projField.columnAttribute = colAtt[0];
                if( ! prop.CanWrite)
                    throw new ApplicationException("Cannot retrieve type "+t.Name+" from SQL - field "+prop.Name+" has no setter");
                projData.fields.Add(projField);

                if(colAtt[0].IsPrimaryKey)
                {
                    projData.keyColumnName = colAtt[0].Name;
                }
            }

            //now we are looking for '[AutoGenId] protected int productId':
            MemberInfo[] members = t.FindMembers(MemberTypes.Field, BindingFlags.Instance|BindingFlags.NonPublic, null, null);
            foreach(FieldInfo field in members.OfType<FieldInfo>())
            {
                object[] objs = field.GetCustomAttributes(typeof(AutoGenIdAttribute),false);
                List<AutoGenIdAttribute> att = objs.OfType<AutoGenIdAttribute>().ToList();
                if(att.Count==0)
                    continue; //not our field
                projData.autoGenField = field;
                break;
            }
            return projData;
        }

        /// <summary>
        /// given expression {o.Customer} (type Order.Customer), check whether it has [Association] attribute
        /// </summary>
        public static bool IsAssociation(MemberExpression memberExpr, out AssociationAttribute assoc)
        {
            assoc = null;
            if(memberExpr==null)
                return false;
            MemberInfo memberInfo = memberExpr.Member;
            PropertyInfo propInfo = memberInfo as PropertyInfo;
            if(propInfo==null)
                return false;
            return IsAssociation(propInfo,out assoc);
        }

        /// <summary>
        /// given field Order.Customer, check whether it has [Association] attribute
        /// </summary>
        public static bool IsAssociation(PropertyInfo propInfo,out AssociationAttribute assoc)
        {
            object[] objs = propInfo.GetCustomAttributes(typeof(AssociationAttribute),false);
            assoc = objs.OfType<AssociationAttribute>().FirstOrDefault();
            if(assoc!=null && !s_knownAssocs.ContainsKey(assoc))
                s_knownAssocs.Add(assoc, propInfo);
            return assoc!=null;
        }

        /// <summary>
        /// given type EntityMSet{X}, return X.
        /// </summary>
        public static Type ExtractTypeFromMSet(Type t)
        {
            if(t.IsGenericType && t.Name.EndsWith("Set`1")) 
            {
                //EntityMSet
                Type[] t1 = t.GetGenericArguments();
                return t1[0]; //Orders
            }
            return t;
        }

        public static AssociationAttribute FindReverseAssociation(AssociationAttribute assoc1)
        {
            if(!s_knownAssocs.ContainsKey(assoc1))
                throw new ArgumentException("AZrgument assoc1 must come from previous IsAssociation call");

            PropertyInfo propInfo = s_knownAssocs[assoc1];
            Type parentTableType = propInfo.PropertyType;
            parentTableType = ExtractTypeFromMSet(parentTableType);

            AssociationAttribute[] assocs = FindPropertiesWithGivenAttrib<AssociationAttribute>(parentTableType);
            
            string assocName = assoc1.Name; //eg. 'FK_Customers_Orders'
            
            var q = from a in assocs where a.Name==assocName select a;
            AssociationAttribute result = q.FirstOrDefault();
            return result;
        }

#if DEAD_CODE
        public static bool FindReverseAssociation(ReverseAssociation.Part assocPartsIn
            ,ReverseAssociation.Part assocPartsToPopulate)
        {
            if(assocPartsIn==null || assocPartsIn.propInfo==null)
                throw new ArgumentException("Invalid reverseParts");
            PropertyInfo propInfo = assocPartsIn.propInfo;
            Type parentTableType = propInfo.PropertyType;
            parentTableType = ExtractTypeFromMSet(parentTableType);

            assocPartsToPopulate.tableAttrib = AttribHelper.GetTableAttrib(parentTableType);
            if(assocPartsIn.tableAttrib==null)
                return false;
            AssociationAttribute[] assocs = FindPropertiesWithGivenAttrib<AssociationAttribute>(parentTableType);
            
            string assocName = assocPartsIn.assocAttrib.Name; //eg. 'FK_Customers_Orders'
            
            var q = from a in assocs where a.Name==assocName select a;
            assocPartsToPopulate.assocAttrib = q.FirstOrDefault();
            if(assocPartsToPopulate.assocAttrib==null)
                return false;
            return true;
        }
#endif
    }
}
