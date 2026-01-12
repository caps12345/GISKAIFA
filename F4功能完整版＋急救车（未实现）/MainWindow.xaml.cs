using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.UI;
using System;
using System.Windows;
using WpfMapApp2.Models;

namespace WpfMapApp2
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            if (DataContext is MapViewModel vm)
            {
                // 1. Subscribe to navigation requests (for search jumps, lockdown zone positioning)
                vm.RequestNavigation += OnRequestNavigation;

                // 2. Listen to viewpoint changes (only stats in F1 mode)
                MapView.ViewpointChanged += (s, e) =>
                {
                    if (vm.IsF1Active)
                    {
                        var extent = MapView.GetCurrentViewpoint(ViewpointType.BoundingGeometry)?.TargetGeometry as Envelope;
                        if (extent != null) vm.UpdateStatisticsFromGraphics(extent);
                    }
                };
            }

            // Bind click event (F5 Emergency Trigger)
            MapView.GeoViewTapped += OnGeoViewTapped;
        }

        private void OnGeoViewTapped(object sender, Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e)
        {
            MapView.DismissCallout();

            // Only trigger rescue logic in F5 mode on map click
            if (DataContext is MapViewModel vm && vm.CurrentModule == "F5")
            {
                if (e.Location != null)
                {
                    vm.SimulateRescue(e.Location);
                }
            }
        }

        private async void OnRequestNavigation(object sender, NavigationEventArgs e)
        {
            if (MapView == null) return;

            // 1. Area/Polygon zoom (e.g., Lockdown zone generated, District jump)
            if (e.IsDistrictZoom && e.DistrictEnvelope != null)
            {
                // Leave 50px padding to ensure full visibility
                await MapView.SetViewpointGeometryAsync(e.DistrictEnvelope, 50);
            }
            // 2. Point zoom (e.g., Community search location)
            else if (e.ResultItem != null)
            {
                MapView.DismissCallout();

                // Default scale
                double scale = 5000;

                // Dynamically adjust scale in F4 mode based on diffusion radius
                if (DataContext is MapViewModel vm && vm.CurrentModule == "F4")
                {
                    double radiusInMeters = vm.InitialRadius;
                    scale = 5000 + (radiusInMeters / 100);
                }

                await MapView.SetViewpointCenterAsync(e.Center, scale);

                // Show callout (in F1 and F4 modes)
                if (DataContext is MapViewModel vm2)
                {
                    if (vm2.CurrentModule == "F1" || vm2.CurrentModule == "F4")
                    {
                        var definition = new CalloutDefinition(e.ResultItem.Name, e.ResultItem.DetailInfo);
                        MapView.ShowCalloutAt(e.Center, definition);
                    }
                }
            }
        }
    }
}