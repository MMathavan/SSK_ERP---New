using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace SSK_ERP
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");
            //defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }

            // Enable attribute routing
            routes.MapMvcAttributeRoutes();


            // Explicit route for CategoryMaster under Masters namespace
            routes.MapRoute(
                name: "CategoryMaster",
                url: "CategoryMaster/{action}/{id}",
                defaults: new { controller = "CategoryMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for EmployeeMaster under Masters namespace
            routes.MapRoute(
                name: "EmployeeMaster",
                url: "EmployeeMaster/{action}/{id}",
                defaults: new { controller = "EmployeeMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for DepartmentMaster under Masters namespace
            routes.MapRoute(
                name: "DepartmentMaster",
                url: "DepartmentMaster/{action}/{id}",
                defaults: new { controller = "DepartmentMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for DesginationMaster under Masters namespace
            routes.MapRoute(
                name: "DesginationMaster",
                url: "DesginationMaster/{action}/{id}",
                defaults: new { controller = "DesginationMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for LocationMaster under Masters namespace
            routes.MapRoute(
                name: "LocationMaster",
                url: "LocationMaster/{action}/{id}",
                defaults: new { controller = "LocationMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for StateMaster under Masters namespace
            routes.MapRoute(
                name: "StateMaster",
                url: "StateMaster/{action}/{id}",
                defaults: new { controller = "StateMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for RegionMaster under Masters namespace
            routes.MapRoute(
                name: "RegionMaster",
                url: "RegionMaster/{action}/{id}",
                defaults: new { controller = "RegionMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for HSNCodeMaster under Masters namespace
            routes.MapRoute(
                name: "HSNCodeMaster",
                url: "HSNCodeMaster/{action}/{id}",
                defaults: new { controller = "HSNCodeMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for CostFactorMaster under Masters namespace
            routes.MapRoute(
                name: "CostFactorMaster",
                url: "CostFactorMaster/{action}/{id}",
                defaults: new { controller = "CostFactorMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for MaterialTypeMaster under Masters namespace
            routes.MapRoute(
                name: "MaterialTypeMaster",
                url: "MaterialTypeMaster/{action}/{id}",
                defaults: new { controller = "MaterialTypeMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for MaterialGroupMaster under Masters namespace
            routes.MapRoute(
                name: "MaterialGroupMaster",
                url: "MaterialGroupMaster/{action}/{id}",
                defaults: new { controller = "MaterialGroupMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for MaterialMaster under Masters namespace
            routes.MapRoute(
                name: "MaterialMaster",
                url: "MaterialMaster/{action}/{id}",
                defaults: new { controller = "MaterialMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for PackingMaster under Masters namespace
            routes.MapRoute(
                name: "PackingMaster",
                url: "PackingMaster/{action}/{id}",
                defaults: new { controller = "PackingMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for CustomerMaster under Masters namespace
            routes.MapRoute(
                name: "CustomerMaster",
                url: "CustomerMaster/{action}/{id}",
                defaults: new { controller = "CustomerMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for SupplierMaster under Masters namespace
            routes.MapRoute(
                name: "SupplierMaster",
                url: "SupplierMaster/{action}/{id}",
                defaults: new { controller = "SupplierMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for CurrencyMaster under Masters namespace
            routes.MapRoute(
                name: "CurrencyMaster",
                url: "CurrencyMaster/{action}/{id}",
                defaults: new { controller = "CurrencyMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for CompanyMaster under Masters namespace
            routes.MapRoute(
                name: "CompanyMaster",
                url: "CompanyMaster/{action}/{id}",
                defaults: new { controller = "CompanyMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for AccountGroupMaster under Masters namespace
            routes.MapRoute(
                name: "AccountGroupMaster",
                url: "AccountGroupMaster/{action}/{id}",
                defaults: new { controller = "AccountGroupMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for AccountHeadMaster under Masters namespace
            routes.MapRoute(
                name: "AccountHeadMaster",
                url: "AccountHeadMaster/{action}/{id}",
                defaults: new { controller = "AccountHeadMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for BloodGroupMaster under Masters namespace
            routes.MapRoute(
                name: "BloodGroupMaster",
                url: "BloodGroupMaster/{action}/{id}",
                defaults: new { controller = "BloodGroupMaster", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers.Masters" }
            );

            // Explicit route for SalesOrder under main controllers namespace
            routes.MapRoute(
                name: "SalesOrder",
                url: "SalesOrder/{action}/{id}",
                defaults: new { controller = "SalesOrder", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers" }
            );

            // Explicit route for PurchaseOrder under main controllers namespace
            routes.MapRoute(
                name: "PurchaseOrder",
                url: "PurchaseOrder/{action}/{id}",
                defaults: new { controller = "PurchaseOrder", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "SSK_ERP.Controllers" }
            );

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Account", action = "Login", id = UrlParameter.Optional }
            );

            routes.MapRoute(
                name: "Members",
                url: "Members/{action}/{id}",
                defaults: new { controller = "Members", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}
