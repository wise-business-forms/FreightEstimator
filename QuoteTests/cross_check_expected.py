"""Independent check of the pinned numbers in Tests/Recorded/quotes/expected/.

Recomputes every pinned Calculated Rate from the recorded carrier response, the charge schedule the
tests seed (QuoteTests/SandboxPlantCharges.KnownSchedule) and the documented formulas, without using the
app's code, and lists every service where the app disagrees. Run from the repo root:

    python QuoteTests/cross_check_expected.py

Last run (2026-10-28): 82 UPS services, 79 agree; the 3 that differ are GRID 2 non-CWT Ground Freight
(cases 444536, 444595, 444597), where the app applies the plant markup and GRID 1 would not. 28 LTL
services, 0 differ. If you change KnownSchedule, change the tables below to match.
"""
import base64
import glob
import json
import os
import re

QUOTES = os.path.join(os.path.dirname(__file__), '..', 'Tests', 'Recorded', 'quotes')

UPS = {'pp': 2.00, 'ps': 5.00, 'NextDayAir': 3, 'SecondDayAir': 4, 'Ground': 5, 'ThreeDaySelect': 6,
       'NextDayAirSaver': 7, 'NextDayAirEarlyAM': 8, 'SecondDayAirAM': 9}
CWT = {'pp': 1.00, 'ps': 3.00, 'NextDayAir': 10, 'SecondDayAir': 11, 'Ground': 12, 'ThreeDaySelect': 13,
       'NextDayAirSaver': 14, 'NextDayAirEarlyAM': 15, 'SecondDayAirAM': 16}
GF = {'pp': 2.00, 'ps': 5.00, 'Ground': 5}
TI = {'pp': 3.00, 'ps': 6.00, 'markup': 7.5}

SERVICE_CODE = {'UPSNextDayAir': '01', 'UPS2ndDayAir': '02', 'UPSGround': '03', 'UPS3DaySelect': '12',
                'NextDayAirSaver': '13', 'NextDayAirEarlyAM': '14', 'SecondDayAirAM': '59'}
MARKUP_COLUMN = {'UPSNextDayAir': 'NextDayAir', 'UPS2ndDayAir': 'SecondDayAir', 'UPSGround': 'Ground',
                 'UPS3DaySelect': 'ThreeDaySelect', 'NextDayAirSaver': 'NextDayAirSaver',
                 'NextDayAirEarlyAM': 'NextDayAirEarlyAM', 'SecondDayAirAM': 'SecondDayAirAM'}
AIR, GROUND = {'01', '02', '13', '59', '14'}, {'03', '12'}


def check_ups(case, expected):
    packages = case['packages']
    last = case['lastPackageWeight']
    billing = case['packageWeight'] * (packages - 1) + last if last > 0 else case['packageWeight'] * packages
    shipments = case['response']['RateResponse']['RatedShipment']
    shipments = shipments if isinstance(shipments, list) else [shipments]
    by_code = {s['Service']['Code']: s for s in shipments}
    differ = []
    for name, got in expected.items():
        if case['kind'] == 'parcel':
            code = SERVICE_CODE[name]
            s = by_code[code]
            published = float(s['TotalCharges']['MonetaryValue'])
            negotiated = float(s.get('NegotiatedRateCharges', {}).get('TotalCharge', {}).get('MonetaryValue', published))
            eligible = (code in AIR and billing >= 100 and packages >= 2) or (code in GROUND and billing >= 200 and packages >= 2)
            schedule = CWT if eligible else UPS
            want = (negotiated * (1 + schedule[MARKUP_COLUMN[name]] / 100) if eligible else published) + schedule['pp'] * packages + schedule['ps']
        else:
            s = shipments[0]
            published = float(s['TotalCharges']['MonetaryValue'])
            negotiated = float(s['NegotiatedRateCharges']['TotalCharge']['MonetaryValue'])
            eligible = billing >= 200 and packages >= 2
            want = (negotiated * (1 + GF['Ground'] / 100) if eligible else published) + GF['pp'] * packages + GF['ps']
        flag_ok = (got['cwt'].upper() in ('YES', 'TRUE')) == eligible
        if abs(got['calculatedRate'] - want) > 0.0006 or not flag_ok:
            differ.append((case['kind'], name, got['calculatedRate'], round(want, 4)))
    return len(expected), differ


def check_ltl(case, expected):
    differ = []
    for name, got in expected.items():
        want = '${:,.2f}'.format(got['carrierRate'] * (1 + TI['markup'] / 100) + TI['pp'] * case['packages'] + TI['ps'])
        if want != got['calculatedRate']:
            differ.append(('ltl', name, got['calculatedRate'], want))
    sheets = re.findall(r'<PriceSheet[ >].*?</PriceSheet>',
                        base64.b64decode(re.search(r'<data>(.*?)</data>', case['responseXml'], re.S).group(1)).decode(), re.S)
    if len(sheets) != len(expected):
        differ.append(('ltl', 'price sheets vs quoted services', len(sheets), len(expected)))
    return len(expected), differ


def main():
    checked = 0
    for path in sorted(glob.glob(os.path.join(QUOTES, '*.json'))):
        case = json.load(open(path, encoding='utf-8'))
        expected = json.load(open(os.path.join(QUOTES, 'expected', os.path.basename(path)), encoding='utf-8'))
        count, differ = (check_ltl if case['kind'] == 'ltl' else check_ups)(case, expected)
        checked += count
        for d in differ:
            print('DIFF', os.path.basename(path), *d)
    print(checked, 'services checked')


if __name__ == '__main__':
    main()
