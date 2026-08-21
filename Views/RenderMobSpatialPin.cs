using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Numerics;
using MMONavigator.Controls;
using MMONavigator.Models;

namespace MMONavigator.Views;

public partial class ThreeDMapWindow : ChildWindow
    {
        // -------------------------------------------------------------------
        // MAIN RENDER METHOD: Renders 2D Map Pin + 3D Ghost Assembly
        // -------------------------------------------------------------------
        public void RenderMobSpatialPin(MobData mob, float activeMapZ, Vector3 playerPosition, Model3DGroup worldGroup)
        {
            if (mob == null || worldGroup == null) return;

            // 1. Calculate 3D points
            Point3D mapProjectionPoint = new Point3D(mob.X, mob.Y, activeMapZ);
            Point3D actual3DPoint = new Point3D(mob.X, mob.Y, mob.Z);

            // 2. Always draw the flat marker/pin on the 2D map surface
            GeometryModel3D mapMarker = CreateFlatMapMarker(mapProjectionPoint, mob.IsSelected ? System.Windows.Media.Colors.Yellow : System.Windows.Media.Colors.Cyan);
            worldGroup.Children.Add(mapMarker);

            // 3. If selected, render the 3D Ghost Assembly (Tether Line + Wireframe Sphere + Text Tag)
            if (mob.IsSelected)
            {
                // A. Vertical Tether Line connecting Map Plane to True Z
                GeometryModel3D tetherLine = CreateVerticalTetherLine(mapProjectionPoint, actual3DPoint, Colors.Cyan);
                worldGroup.Children.Add(tetherLine);

                // B. Floating 3D Wireframe Ghost Sphere at actual (X, Y, Z)
                GeometryModel3D ghostSphere = CreateWireframeSphere(actual3DPoint, radius: 2.5f, Colors.Cyan, opacity: 0.35);
                worldGroup.Children.Add(ghostSphere);

                // C. Relative Height Tag & Name Label
                float deltaZ = mob.Z - playerPosition.Z;
                string heightText = deltaZ >= 0 ? $"+{deltaZ:F1}m" : $"{deltaZ:F1}m";
                string fullLabel = $"{mob.Name} ({heightText})";

                GeometryModel3D billboardTag = CreateBillboardTextTag(actual3DPoint, fullLabel, Colors.White);
                worldGroup.Children.Add(billboardTag);
            }
        }

        // -------------------------------------------------------------------
        // HELPER 1: Creates a flat 2D Pin Quad on the Map Plane
        // -------------------------------------------------------------------
        private GeometryModel3D CreateFlatMapMarker(Point3D position, System.Windows.Media.Color color)
        {
            MeshGeometry3D quadMesh = new MeshGeometry3D();
            double size = 1.5; // Pin size

            quadMesh.Positions.Add(new Point3D(position.X - size, position.Y - size, position.Z + 0.1));
            quadMesh.Positions.Add(new Point3D(position.X + size, position.Y - size, position.Z + 0.1));
            quadMesh.Positions.Add(new Point3D(position.X + size, position.Y + size, position.Z + 0.1));
            quadMesh.Positions.Add(new Point3D(position.X - size, position.Y + size, position.Z + 0.1));

            quadMesh.TriangleIndices.Add(0); quadMesh.TriangleIndices.Add(1); quadMesh.TriangleIndices.Add(2);
            quadMesh.TriangleIndices.Add(0); quadMesh.TriangleIndices.Add(2); quadMesh.TriangleIndices.Add(3);

            DiffuseMaterial material = new DiffuseMaterial(new SolidColorBrush(color));
            return new GeometryModel3D { Geometry = quadMesh, Material = material, BackMaterial = material };
        }

        // -------------------------------------------------------------------
        // HELPER 2: Creates a Vertical Tether Line (Thin Cylinder/Quad)
        // -------------------------------------------------------------------
        private GeometryModel3D CreateVerticalTetherLine(Point3D start, Point3D end, System.Windows.Media.Color lineColor)
        {
            MeshGeometry3D lineMesh = new MeshGeometry3D();
            double thickness = 0.2;

            lineMesh.Positions.Add(new Point3D(start.X - thickness, start.Y, start.Z));
            lineMesh.Positions.Add(new Point3D(start.X + thickness, start.Y, start.Z));
            lineMesh.Positions.Add(new Point3D(end.X + thickness, end.Y, end.Z));
            lineMesh.Positions.Add(new Point3D(end.X - thickness, end.Y, end.Z));

            lineMesh.TriangleIndices.Add(0); lineMesh.TriangleIndices.Add(1); lineMesh.TriangleIndices.Add(2);
            lineMesh.TriangleIndices.Add(0); lineMesh.TriangleIndices.Add(2); lineMesh.TriangleIndices.Add(3);

            DiffuseMaterial material = new DiffuseMaterial(new SolidColorBrush(lineColor) { Opacity = 0.7 });
            return new GeometryModel3D { Geometry = lineMesh, Material = material, BackMaterial = material };
        }

        // -------------------------------------------------------------------
        // HELPER 3: Creates a 3D Wireframe/Geodesic Ghost Sphere
        // -------------------------------------------------------------------
        private GeometryModel3D CreateWireframeSphere(Point3D center, double radius, System.Windows.Media.Color color, double opacity)
        {
            MeshGeometry3D sphereMesh = new MeshGeometry3D();
            int latitudeSegments = 8;
            int longitudeSegments = 12;

            for (int lat = 0; lat <= latitudeSegments; lat++)
            {
                double theta = lat * Math.PI / latitudeSegments;
                double sinTheta = Math.Sin(theta);
                double cosTheta = Math.Cos(theta);

                for (int lon = 0; lon <= longitudeSegments; lon++)
                {
                    double phi = lon * 2 * Math.PI / longitudeSegments;
                    double x = center.X + radius * sinTheta * Math.Cos(phi);
                    double y = center.Y + radius * sinTheta * Math.Sin(phi);
                    double z = center.Z + radius * cosTheta;

                    sphereMesh.Positions.Add(new Point3D(x, y, z));
                }
            }

            for (int lat = 0; lat < latitudeSegments; lat++)
            {
                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    int current = (lat * (longitudeSegments + 1)) + lon;
                    int next = current + longitudeSegments + 1;

                    sphereMesh.TriangleIndices.Add(current);
                    sphereMesh.TriangleIndices.Add(next);
                    sphereMesh.TriangleIndices.Add(current + 1);

                    sphereMesh.TriangleIndices.Add(next);
                    sphereMesh.TriangleIndices.Add(next + 1);
                    sphereMesh.TriangleIndices.Add(current + 1);
                }
            }

            DiffuseMaterial material = new DiffuseMaterial(new SolidColorBrush(color) { Opacity = opacity });
            return new GeometryModel3D { Geometry = sphereMesh, Material = material, BackMaterial = material };
        }

        // -------------------------------------------------------------------
        // HELPER 4: Renders Text Label directly into 3D Space using VisualBrush
        // -------------------------------------------------------------------
        private GeometryModel3D CreateBillboardTextTag(Point3D position, string text, System.Windows.Media.Color textColor)
        {
            // 1. Create a 2D TextBlock with background
            TextBlock textBlock = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(textColor),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(6, 3, 6, 3)
            };

            VisualBrush textBrush = new VisualBrush(textBlock);

            // 2. Create a small 3D Quad facing the camera plane
            MeshGeometry3D quadMesh = new MeshGeometry3D();
            double w = 6.0;
            double h = 2.0;

            quadMesh.Positions.Add(new Point3D(position.X - w/2, position.Y, position.Z + 3.0));
            quadMesh.Positions.Add(new Point3D(position.X + w/2, position.Y, position.Z + 3.0));
            quadMesh.Positions.Add(new Point3D(position.X + w/2, position.Y, position.Z + 3.0 + h));
            quadMesh.Positions.Add(new Point3D(position.X - w/2, position.Y, position.Z + 3.0 + h));

            quadMesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
            quadMesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
            quadMesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
            quadMesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));

            quadMesh.TriangleIndices.Add(0); quadMesh.TriangleIndices.Add(1); quadMesh.TriangleIndices.Add(2);
            quadMesh.TriangleIndices.Add(0); quadMesh.TriangleIndices.Add(2); quadMesh.TriangleIndices.Add(3);

            DiffuseMaterial material = new DiffuseMaterial(textBrush);
            return new GeometryModel3D { Geometry = quadMesh, Material = material, BackMaterial = material };
        }
    }