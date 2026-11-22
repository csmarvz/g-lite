"""Print the onboard profile layout of each 0x8100 profile format, read from the
native code of logi_nethidppio.dll (the HID++ library inside Logitech's Onboard
Memory Manager). Reads the file only; runs nothing from it.

Usage: python -I layouts.py logi_nethidppio.dll

How it works, so it can be redone on another version:
- The library keeps its RTTI. Each `*_component@onboard_profiles` class name leads
  to its type descriptor, complete object locator and vtable.
- Code that builds a profile layout loads a component's vtable with
  `lea reg, [rip+disp]`, stores the component's offset, size and extra
  parameters as immediates, sets the component id in r8d and calls add_component.
- The dispatcher reads the format byte (`movzx eax, byte [reg+9]`, `dec eax`,
  `cmp eax, 9`) and jumps through a table of 32-bit RVAs, one case per format.
Only the few instruction forms those builders use are decoded; anything else is
skipped a byte at a time, which is enough here but not a general disassembler.
"""
import re
import struct
import sys

data = open(sys.argv[1], 'rb').read()
header = struct.unpack_from('<I', data, 0x3C)[0]
section_count = struct.unpack_from('<H', data, header + 6)[0]
optional_size = struct.unpack_from('<H', data, header + 20)[0]
image_base = struct.unpack_from('<Q', data, header + 24 + 24)[0]
sections = []
for index in range(section_count):
    entry = header + 24 + optional_size + 40 * index
    name = data[entry:entry + 8].rstrip(b'\0').decode()
    virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from('<IIII', data, entry + 8)
    sections.append((name, virtual_address, virtual_size, raw_offset, raw_size))


def offset_to_rva(offset):
    for name, virtual_address, virtual_size, raw_offset, raw_size in sections:
        if raw_offset <= offset < raw_offset + raw_size:
            return virtual_address + offset - raw_offset


def rva_to_offset(rva):
    for name, virtual_address, virtual_size, raw_offset, raw_size in sections:
        if virtual_address <= rva < virtual_address + raw_size:
            return raw_offset + rva - virtual_address


def section(name):
    return [entry for entry in sections if entry[0] == name][0]


vtables = {}
for match in re.finditer(rb'\.\?AV([a-z0-9_]+)@onboard_profiles@hidppio@logi@@\x00', data):
    descriptor = offset_to_rva(match.start() - 16)
    for reference in re.finditer(re.escape(struct.pack('<I', descriptor)), data):
        locator = reference.start() - 12
        if struct.unpack_from('<I', data, locator)[0] != 1:
            continue
        pointer = struct.pack('<Q', image_base + offset_to_rva(locator))
        for vtable in re.finditer(re.escape(pointer), data):
            vtables[offset_to_rva(vtable.start()) + 8] = match.group(1).decode().replace('_component', '')

pdata = section('.pdata')
functions = [struct.unpack_from('<III', data, pdata[3] + index * 12)[:2] for index in range(pdata[2] // 12)]


def function_containing(rva):
    for begin, end in functions:
        if begin <= rva < end:
            return begin, end


def memory_operand(code, position):
    modrm = code[position]
    mode, base = modrm >> 6, modrm & 7
    if base == 4:
        if mode == 1:
            return 'rsp%+d' % struct.unpack_from('<b', code, position + 2)[0], 3
        if mode == 2:
            return 'rsp%+d' % struct.unpack_from('<i', code, position + 2)[0], 6
    if mode == 1:
        return 'r%d%+d' % (base, struct.unpack_from('<b', code, position + 1)[0]), 2
    if mode == 2:
        return 'r%d%+d' % (base, struct.unpack_from('<i', code, position + 1)[0]), 5
    return None, 0


def events(begin, end):
    """Yield (rva, kind, value): 'store' immediates (a store of rdi counts as 0
    once `xor edi, edi` has run), 'vtable' loads, 'id' (mov r8d) and 'call' targets."""
    position = rva_to_offset(begin)
    stop = rva_to_offset(end)
    edi_is_zero = False
    while position < stop:
        code = data[position:position + 16]
        rva = offset_to_rva(position)
        if code[0] == 0x48 and code[1] == 0xC7 and code[2] >> 6 in (1, 2):
            operand, length = memory_operand(code, 2)
            if operand:
                yield rva, 'store', struct.unpack_from('<i', code, 2 + length)[0]
                position += 2 + length + 4
                continue
        if code[0] == 0x33 and code[1] == 0xFF:
            edi_is_zero = True
            position += 2
            continue
        if code[0] == 0xBF:
            edi_is_zero = False
            position += 5
            continue
        if edi_is_zero and code[0] == 0x48 and code[1] == 0x89 and (code[2] >> 3) & 7 == 7 and code[2] >> 6 in (1, 2):
            operand, length = memory_operand(code, 2)
            if operand:
                yield rva, 'store', 0
                position += 2 + length
                continue
        if code[0] in (0x48, 0x4C) and code[1] == 0x8D and code[2] & 0xC7 == 0x05:
            target = rva + 7 + struct.unpack_from('<i', code, 3)[0]
            if target in vtables:
                yield rva, 'vtable', vtables[target]
            position += 7
            continue
        if code[0] == 0x41 and code[1] == 0xB8:
            yield rva, 'id', struct.unpack_from('<i', code, 2)[0]
            position += 6
            continue
        if code[0] == 0x45 and code[1] == 0x33 and code[2] == 0xC0:
            yield rva, 'id', 0
            position += 3
            continue
        if code[0] == 0xE8:
            yield rva, 'call', rva + 5 + struct.unpack_from('<i', code, 1)[0]
            position += 5
            continue
        position += 1


def components(begin, end, builders):
    """List what a builder adds: (component, offset, size, id, extra) and the builders it calls first."""
    added, calls = [], []
    stores, component, component_id = [], None, None
    for rva, kind, value in events(begin, end):
        if kind == 'store':
            stores.append(value)
        elif kind == 'vtable':
            component = value
        elif kind == 'id':
            component_id = value
        elif kind == 'call':
            if value in builders:
                calls.append(value)
            elif component and len(stores) >= 2:
                added.append((component, stores[0], stores[1], component_id, stores[2:]))
            stores, component_id = [], None
    return added, calls


# OnboardProfile.ComponentId in the library's managed layer. When the id is
# known it names the component, since a builder can reuse a vtable kept in a
# register and the last vtable load then belongs to another component.
component_names = {
    0: 'report_rate', 1: 'dpi_v1', 2: 'color', 3: 'power_mode', 4: 'angle_snapping',
    5: 'button_fn', 6: 'button_fn (G-shift)', 7: 'profile_name', 8: 'lighting_x8070 (logo)',
    9: 'lighting_x8070 (side)', 10: 'write_counter', 11: 'power_save_timeout',
    12: 'power_off_timeout', 13: 'lighting_x8071 (logo, power save)',
    14: 'lighting_x8071 (side, power save)', 15: 'lighting_x8071 (cluster 0 active)',
    16: 'lighting_x8071 (cluster 0 passive)', 17: 'lighting_x8071 (cluster 1 active)',
    18: 'lighting_x8071 (cluster 1 passive)', 19: 'lighting_flags', 20: 'report_rate_wireless',
    21: 'report_rate_wired', 22: 'dpi_v6', 23: 'dpi_delta', 24: 'bunny_hopping',
    25: 'analog_button', 26: 'dpi_v10_y',
}
ids_by_class = {}
for component_id, name in component_names.items():
    ids_by_class.setdefault(name.split(' ')[0], []).append(component_id)

text = section('.text')
references = {}
for position in range(text[3], text[3] + text[4] - 7):
    if data[position] in (0x48, 0x4C) and data[position + 1] == 0x8D and data[position + 2] & 0xC7 == 0x05:
        rva = offset_to_rva(position)
        target = rva + 7 + struct.unpack_from('<i', data, position + 3)[0]
        if target in vtables:
            references.setdefault(function_containing(rva), set()).add(vtables[target])
# A component's constructor also stores the base class's vtable; a builder doesn't.
builders = {begin: end for (begin, end), names in references.items() if 'profile' not in names}

dispatch = re.search(rb'\x0F\xB6.\x09\xFF\xC8\x83\xF8(.)\x0F\x87....\x48\x98\x48\x8D\x15....\x8B\x8C\x82(....)', data, re.S)
case_count = dispatch.group(1)[0] + 1
table = struct.unpack_from('<I', dispatch.group(2))[0]
cases = struct.unpack_from('<%dI' % case_count, data, rva_to_offset(table))
dispatcher_begin, dispatcher_end = function_containing(offset_to_rva(dispatch.start()))
case_ends = sorted(set(cases)) + [dispatcher_end]


def flatten(builder):
    added, calls = components(builder, builders[builder], builders)
    result = []
    for called in calls:
        result += flatten(called)
    return result + added


print('Component offsets by profile format (format byte at +9 of the 0x8100 fn 0 reply)')
print('offset, size, component [id] (more constructor arguments)\n')
for format_number, case in enumerate(cases, 1):
    case_end = min(end for end in case_ends if end > case)
    added, calls = components(case, case_end, builders)
    layout = []
    for called in calls:
        layout += flatten(called)
    layout += added
    print('Format %d (case at rva %#x, builders %s)' % (format_number, case, ', '.join('%#x' % called for called in calls) or 'none'))
    if not layout:
        print('  no layout: the case throws "Profile format not supported"')
    for component, offset, size, component_id, extra in sorted(layout, key=lambda item: item[1]):
        if component_id is None and len(ids_by_class.get(component, [])) == 1:
            component_id = ids_by_class[component][0]
        name = component_names.get(component_id, component)
        print('  %#05x %3d  %-36s %s%s' % (offset, size, name,
                                          '[%d]' % component_id if component_id is not None else '[?]',
                                          ' (%s)' % ', '.join(str(value) for value in extra) if extra else ''))
    print()
