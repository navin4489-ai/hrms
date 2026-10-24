import 'package:flutter/material.dart';

/// Status -> color/icon/text mapping. Never color-only: every chip carries a
/// label and most carry an icon (accessibility, NFR-006).
class StatusVisual {
  const StatusVisual(this.color, this.icon, this.label);

  final Color color;
  final IconData icon;
  final String label;
}

/// Maps server enum/status strings to a [StatusVisual]. Unknown statuses fall
/// back to a neutral chip with the friendly enum label.
StatusVisual statusVisual(String? status, {ColorScheme? scheme}) {
  final s = (status ?? '').trim();
  switch (s) {
    // Request / workflow states
    case 'Pending':
    case 'Submitted':
    case 'WaitingOnEmployee':
    case 'WaitingOnHR':
    case 'ManagerApproved':
    case 'FinanceApproved':
    case 'Draft':
      return StatusVisual(
        const Color(0xFFF57F17),
        Icons.hourglass_top_rounded,
        _label(s),
      );
    case 'Approved':
    case 'Published':
    case 'Verified':
    case 'Completed':
    case 'Resolved':
    case 'Paid':
      return const StatusVisual(
        Color(0xFF2E7D32),
        Icons.check_circle_rounded,
        '', // label filled below
      );
    case 'Rejected':
    case 'Expired':
    case 'Cancelled':
    case 'CancelledByEmployee':
    case 'Closed':
      return const StatusVisual(Color(0xFFC62828), Icons.cancel_rounded, '');
    case 'Returned':
      return const StatusVisual(
        Color(0xFF607D8B),
        Icons.assignment_returned_rounded,
        '',
      );
    case 'Reopened':
    case 'ExpiringSoon':
    case 'ReturnRequested':
      return const StatusVisual(Color(0xFF0277BD), Icons.undo_rounded, '');

    // Attendance statuses
    case 'Present':
      return const StatusVisual(Color(0xFF2E7D32), Icons.work_rounded, '');
    case 'Absent':
      return const StatusVisual(
        Color(0xFFC62828),
        Icons.free_cancellation_rounded,
        '',
      );
    case 'WeekOff':
      return const StatusVisual(Color(0xFF607D8B), Icons.weekend_rounded, '');
    case 'Holiday':
      return const StatusVisual(
        Color(0xFF7B1FA2),
        Icons.celebration_rounded,
        '',
      );
    case 'Leave':
      return const StatusVisual(
        Color(0xFF0277BD),
        Icons.beach_access_rounded,
        '',
      );
    case 'HalfDay':
      return const StatusVisual(Color(0xFFF57F17), Icons.adjust_rounded, '');
    case 'Late':
      return const StatusVisual(Color(0xFFE65100), Icons.near_me_rounded, '');
    case 'MissingPunch':
      return const StatusVisual(Color(0xFFC62828), Icons.link_off_rounded, '');
    case 'NotMarked':
      return const StatusVisual(
        Color(0xFF9E9E9E),
        Icons.remove_circle_outline_rounded,
        '',
      );
    case 'Open':
      return const StatusVisual(
        Color(0xFF0277BD),
        Icons.folder_open_rounded,
        '',
      );
    case 'Assigned':
      return const StatusVisual(
        Color(0xFF2E7D32),
        Icons.laptop_mac_rounded,
        '',
      );
    case 'Enrolled':
      return const StatusVisual(Color(0xFF0277BD), Icons.school_rounded, '');
    case 'NotEnrolled':
      return const StatusVisual(Color(0xFF9E9E9E), Icons.school_outlined, '');
    case 'Overdue':
      return const StatusVisual(
        Color(0xFFC62828),
        Icons.warning_amber_rounded,
        '',
      );
    default:
      return StatusVisual(
        const Color(0xFF607D8B),
        Icons.info_outline_rounded,
        _label(s),
      );
  }
}

String _label(String s) {
  if (s.isEmpty) return '—';
  final withBreaks = s
      .replaceAllMapped(RegExp(r'([a-z0-9])([A-Z])'), (m) => '${m[1]} ${m[2]}')
      .replaceAllMapped(
        RegExp(r'([A-Z]+)([A-Z][a-z])'),
        (m) => '${m[1]} ${m[2]}',
      );
  return withBreaks;
}

/// Colored chip with icon + text label for any status enum.
class StatusChip extends StatelessWidget {
  const StatusChip({
    super.key,
    required this.status,
    this.dense = false,
    this.visual,
  });

  final String? status;

  /// Compact padding for dense lists.
  final bool dense;

  /// Optional override (e.g. dashboard request types).
  final StatusVisual? visual;

  @override
  Widget build(BuildContext context) {
    final v =
        visual ?? statusVisual(status, scheme: Theme.of(context).colorScheme);
    final label = v.label.isNotEmpty && v.label != '—'
        ? v.label
        : _label(status ?? '');
    return Container(
      padding: EdgeInsets.symmetric(
        horizontal: dense ? 8 : 10,
        vertical: dense ? 3 : 5,
      ),
      decoration: BoxDecoration(
        color: v.color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: v.color.withValues(alpha: 0.5)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(v.icon, size: dense ? 13 : 15, color: v.color),
          const SizedBox(width: 5),
          Text(
            label,
            style: TextStyle(
              color: v.color,
              fontSize: dense ? 11.5 : 13,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}
