"""Writes bridge-basic.ids with the Python converter code.

Run from the repository root with the Python environment of the C# port:
    python csharp/tests/IDS4ALL.Tests/Golden/generate_bridge_golden.py

BridgeTests.cs builds the same specifications as records and compares the output with this file.
"""
import os
import sys

root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '..'))
sys.path.insert(0, root)

from ifctester import ids
from custom_functions import add_comment_to_xml, create_ids_specifications

COMMENT = ' Created with the IDS4ALL Converter test. '

pset = {'PropertySet': ['Pset_WallCommon']}

spec_walls = {
    'general': {'Phase': ['P1', 'P2'], 'Role': ['Architect'], 'Usecase': ['Permit']},
    'spec': {'SpecificationName': ['Walls'], 'SpecificationCardinality': ['required'],
             'SpecificationIfcVersion': ['IFC4', 'IFC4X3_ADD2']},
    'app': [{'Entity': ['IFCWALL'], 'PredefinedType': ['STANDARD']}],
    'req': [
        {**pset, 'Property': ['FireRating'], 'PropertyDatatype': ['IFCLABEL'],
         'PropertyValue': ['REI60', 'REI90'], 'PropertyURI': ['https://example.org/p'], 'Cardinality': ['required']},
        {**pset, 'Property': ['ThermalTransmittance'], 'PropertyDatatype': ['IFCTHERMALTRANSMITTANCEMEASURE'],
         'PropertyValue': [r'\<=0,5'], 'Cardinality': ['required']},
        {**pset, 'Property': ['LoadBearingCapacity'], 'PropertyDatatype': ['IFCREAL'],
         'PropertyValue': [r'\>1.5'], 'Cardinality': ['optional']},
        {**pset, 'Property': ['Width'], 'PropertyDatatype': ['IFCLENGTHMEASURE'],
         'PropertyValue': [r'\>=0,2']},
        {**pset, 'Property': ['Height'], 'PropertyDatatype': ['IFCLENGTHMEASURE'],
         'PropertyValue': [r'\<3']},
        {**pset, 'Property': ['Reference'], 'PropertyDatatype': ['IFCLABEL'],
         'PropertyValue': ['pattern=^W[0-9]+$'], 'Cardinality': ['optional']},
        {**pset, 'Property': ['Code'], 'PropertyDatatype': ['IFCLABEL'],
         'PropertyValue': ['length>=3']},
        {**pset, 'Property': ['Mark'], 'PropertyDatatype': ['IFCLABEL'],
         'PropertyValue': ['length<=8']},
        {**pset, 'Property': ['Tag'], 'PropertyDatatype': ['IFCLABEL'],
         'PropertyValue': ['length=5']},
        {**pset, 'Property': ['IsExternal'], 'PropertyDatatype': ['IFCBOOLEAN'],
         'PropertyValue': ['TRUE'], 'Cardinality': ['required']},
        {'Attribute': ['Name'], 'AttributeValue': ['Wall 1'], 'Cardinality': ['required']},
        {'Attribute': ['Description']},
        {'Classification': ['21.21'], 'ClassificationSystem': ['Uniclass'],
         'ClassificationURI': ['https://example.org/c'], 'Cardinality': ['required']},
        {'PartOfEntity': ['IFCBUILDINGSTOREY'], 'PartOfRelation': ['IFCRELCONTAINEDINSPATIALSTRUCTURE'],
         'Cardinality': ['required']},
        {'Material': ['Concrete', 'Steel'], 'MaterialURI': ['https://example.org/m'], 'Cardinality': ['prohibited']},
        {'Entity': ['IFCWALL'], 'Description': ['Wall entity']},
    ],
}

spec_slabs = {
    'general': {},
    'spec': {'SpecificationCardinality': ['prohibited'], 'SpecificationIfcVersion': ['IFC4']},
    'app': [{'Entity': ['IFCSLAB'], 'PredefinedType': ['FLOOR']}],
    'req': [{'Attribute': ['Name']}],
}

spec_enum = {
    'general': {'Phase': ['P1']},
    'spec': {'SpecificationIfcVersion': ['IFC2X3']},
    'app': [{'Entity': ['IFCWALL', 'IFCSLAB']}, {**pset, 'Property': ['IsExternal']}],
    'req': [{'Material': ['Concrete']}],
}

document = ids.Ids(title='Bridge test', copyright='TU Wien', version='1.0', description='Bridge test file',
                   author='test@example.org', date='2026-01-01', purpose='Role: Architect', milestone='P1, P2')
create_ids_specifications(document, [spec_walls, spec_slabs, spec_enum])

out = os.path.join(os.path.dirname(__file__), 'bridge-basic.ids')
document.to_xml(out)
add_comment_to_xml(out, COMMENT)
print(out)
