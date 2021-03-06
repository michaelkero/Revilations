﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows;
using Autodesk.Revit.DB.Structure;

namespace Revilations.Revit {

    [Transaction(TransactionMode.Manual)]
    public class RevilationCommand : IExternalCommand {
        
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
            try {
                var uidoc = commandData.Application.ActiveUIDocument;

                var allViews = CollectAllViews(uidoc.Document);
                var allDetailComponents = CollectAllDetailComponents(uidoc.Document);
                var control = new RevilationsInputControl(allViews, allDetailComponents);
                while (true) {
                    control.ShowDialog();
                    if (control.SelectElements) {
                        control.SetElements(uidoc.Selection.PickObjects(ObjectType.Element).Select(e => e.ElementId));
                        control.SelectElements = false;
                    } else if (control.SelectPads) {
                        control.SetPads(uidoc.Selection.PickObjects(ObjectType.Element).Select(e => e.ElementId));
                        control.SelectPads = false;
                    } else {
                        break;
                    }
                }

                if (control.RunOnClose) {
                    var masterElements = control.SelectedElements;
                    var masterPad = new RevilationPad(masterElements.FirstOrDefault(e => e.Item1.Name.Equals(Revilations.PadFamilySymbolName)).Item1, null);
                    var selectedPads = control.SelectedPads.Select(e => new RevilationPad(e, masterPad));
                    var viewToAddComponentsTo = control.SelectedView;

                    using (var trans = new Transaction(uidoc.Document)) {
                        trans.Start("Revilations");
                        foreach (var masterElement in masterElements) {
                            if (masterElement.Item1.Id.IntegerValue != masterPad.RevitElement.Id.IntegerValue) {
                                var createdElemIds = "";
                                foreach (var pad in selectedPads) {
                                    var createdElem = (masterElement.Item2 == null) ? 
                                        this.CreateCopyElement(masterElement.Item1, masterPad, pad) : 
                                        this.CreateDetailObject(masterElement.Item1, masterElement.Item2, masterPad, pad, viewToAddComponentsTo);

                                    createdElem.LookupParameter("RevilationsParents").Set(masterElement.Item1.Id.IntegerValue.ToString());
                                    createdElem.LookupParameter("RevilationsPadId").Set(pad.RevitElement.Id.IntegerValue.ToString());
                                    createdElemIds = $"{createdElemIds};{createdElem.Id.IntegerValue}";
                                }
                                var placementPt = ((masterElement.Item1.Location is LocationPoint) ? (masterElement.Item1.Location as LocationPoint).Point : (masterElement.Item1.Location as LocationCurve).Curve.Evaluate(0.5, true));
                                masterElement.Item1.LookupParameter("RevilationsChildren").Set(createdElemIds);
                                masterElement.Item1.LookupParameter("RevilationsX").Set($"{placementPt.X}");
                                masterElement.Item1.LookupParameter("RevilationsY").Set($"{placementPt.Y}");
                                masterElement.Item1.LookupParameter("RevilationsZ").Set($"{placementPt.Z}");
                            }
                        }
                        trans.Commit();
                    }
                    return Result.Succeeded;
                } else {
                    return Result.Cancelled;
                }
            } catch (Exception x) {
                TaskDialog.Show("Revilations", $"{x.Message}\n{x.StackTrace}");
                return Result.Failed;
            }
        }

        public List<View> CollectAllViews(Document doc) {
            var fec = new FilteredElementCollector(doc);
            fec.OfCategory(BuiltInCategory.OST_Views);
            var views = new List<View>();
            foreach (var e in fec.ToElements()) {
                if (e is View) {
                    var v = e as View;
                    if (v.ViewType == ViewType.EngineeringPlan || v.ViewType == ViewType.FloorPlan) {
                        views.Add(v);
                    }
                }
            }
            return views;
        }

        public List<FamilySymbol> CollectAllDetailComponents(Document doc) {
            var fec = new FilteredElementCollector(doc);
            fec.OfCategory(BuiltInCategory.OST_DetailComponents);
            fec.OfClass(typeof(FamilySymbol));
            var detailComps = new List<FamilySymbol>();
            foreach (var e in fec.ToElements()) {
                detailComps.Add(e as FamilySymbol);
            }
            return detailComps;
        }

        FamilyInstance CreateCopyElement(FamilyInstance elem, RevilationPad masterPad, RevilationPad pad) {
            var doc = elem.Document;
            var placement = ((elem.Location is LocationPoint) ? (elem.Location as LocationPoint).Point : (elem.Location as LocationCurve).Curve.Evaluate(0.5, true));
            var remappedPoint = pad.CalculatePlacementLocation(placement, masterPad);

            if (!elem.Symbol.IsActive) {
                elem.Symbol.Activate();
            }
            return doc.Create.NewFamilyInstance(remappedPoint, elem.Symbol, StructuralType.NonStructural);

            //return elem.Document.GetElement(ElementTransformUtils.CopyElement(elem.Document, elem.Id, pad.Translation).FirstOrDefault()) as FamilyInstance;
        }

        FamilyInstance CreateDetailObject(FamilyInstance elem, FamilySymbol symbol, RevilationPad masterPad, RevilationPad pad, View viewToPlaceOn) {
            var doc = elem.Document;
            var placement = ((elem.Location is LocationPoint) ? (elem.Location as LocationPoint).Point : (elem.Location as LocationCurve).Curve.Evaluate(0.5, true));
            var remappedPoint = pad.CalculatePlacementLocation(placement, masterPad);

            if (!symbol.IsActive) {
                symbol.Activate();
            }
            return doc.Create.NewFamilyInstance(remappedPoint, symbol, viewToPlaceOn);
        }

        void SetElementDatas(FamilyInstance elem, FamilyInstance parentElement, RevilationPad pad) {
            //set the pad id
            elem.LookupParameter("RevilationsPadId").Set(pad.RevitElement.Id.IntegerValue.ToString());

            //set the parent ids
            var parentIdsParam = elem.LookupParameter("RevilationsParents");
            var parentIdsString = parentIdsParam.AsString();
            var parentIdString = (parentIdsString == null || parentIdsString.Equals(string.Empty)) ? $"{parentElement.Id.IntegerValue}" : $"{parentIdsString};{parentElement.Id.IntegerValue}";
            parentIdsParam.Set(parentIdString);

            //set the child ids
            var childrenIdsParam = parentElement.LookupParameter("RevilationsChildren");
            var childrenIdsString = childrenIdsParam.AsString();
            var childrenIdString = (childrenIdsString == null || childrenIdsString.Equals(string.Empty)) ? $"{elem.Id.IntegerValue}" : $"{childrenIdsString};{elem.Id.IntegerValue}";
            childrenIdsParam.Set(childrenIdString);
        }
    }
}