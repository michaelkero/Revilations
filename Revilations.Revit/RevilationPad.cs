﻿using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Revilations.Revit {

    public class RevilationPad {

        private FamilyInstance revitElement;
        private bool isMaster;
        private Transform transform;

        private XYZ translationVector;
        private double rotation;

        public RevilationPad(FamilyInstance revitElement, RevilationPad masterPad) {
            this.revitElement = revitElement;
            this.isMaster = masterPad == null || this.revitElement == masterPad.RevitElement;
            this.transform = (this.isMaster) ? Transform.Identity : this.CalculateTransform(masterPad);
        }

        public FamilyInstance RevitElement {
            get { return this.revitElement; }
        }

        public Transform Transform {
            get { return this.transform; }
        }

        public XYZ Translation {
            get { return this.translationVector; }
        }

        public double Rotation {
            get { return this.rotation; }
        }

        public XYZ CenterPoint {
            get {
                var bb = this.revitElement.get_BoundingBox(null);
                return (bb.Max + bb.Min)/2.0;
            }   
        }

        Transform CalculateTransform(RevilationPad masterPad) {
            this.translationVector = this.CenterPoint - masterPad.CenterPoint;
            var translationTransform = Transform.CreateTranslation(this.translationVector);
            this.rotation = 0.0;
            var rotationTransform = Transform.CreateRotation(XYZ.BasisZ, this.rotation);
            return translationTransform.Multiply(rotationTransform);
        }
    }
}
