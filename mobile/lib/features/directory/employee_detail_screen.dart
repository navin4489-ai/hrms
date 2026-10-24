import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/directory_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// Employee record (own profile or `employee.view` scope). The server only
/// returns fields the caller may see (FR-EMP-024 privacy rules).
class EmployeeDetailScreen extends ConsumerWidget {
  const EmployeeDetailScreen({super.key, required this.employeeId});

  final String employeeId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final detail = ref.watch(employeeDetailProvider(employeeId));
    final theme = Theme.of(context);

    return AppScaffold(
      title: Strings.employeeDetail,
      body: RefreshIndicator(
        onRefresh: () async =>
            ref.invalidate(employeeDetailProvider(employeeId)),
        child: AsyncBody(
          value: detail,
          onRetry: () => ref.invalidate(employeeDetailProvider(employeeId)),
          builder: (profile) => SingleChildScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    CircleAvatar(
                      radius: 30,
                      backgroundColor: theme.colorScheme.primaryContainer,
                      foregroundColor: theme.colorScheme.onPrimaryContainer,
                      child: Text(
                        profile.initials,
                        style: const TextStyle(
                          fontWeight: FontWeight.w800,
                          fontSize: 20,
                        ),
                      ),
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            profile.displayName,
                            style: theme.textTheme.titleLarge?.copyWith(
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          if (profile.designation != null)
                            Text(
                              profile.designation!,
                              style: theme.textTheme.bodyMedium?.copyWith(
                                color: theme.colorScheme.onSurfaceVariant,
                              ),
                            ),
                        ],
                      ),
                    ),
                    if (profile.employmentStatus != null)
                      StatusChip(status: profile.employmentStatus, dense: true),
                  ],
                ),
                const SizedBox(height: 16),
                SectionCard(
                  title: 'Work',
                  child: Column(
                    children: [
                      InfoRow(
                        label: 'Employee number',
                        value: profile.employeeNumber,
                      ),
                      InfoRow(
                        label: Strings.department,
                        value: profile.department ?? '-',
                      ),
                      InfoRow(
                        label: Strings.location,
                        value: profile.location ?? '-',
                      ),
                      InfoRow(
                        label: Strings.designation,
                        value: profile.designation ?? '-',
                      ),
                      InfoRow(
                        label: Strings.manager,
                        value: profile.managerName ?? '-',
                      ),
                      InfoRow(
                        label: Strings.workEmail,
                        value: profile.workEmail ?? '-',
                      ),
                      InfoRow(
                        label: Strings.joiningDate,
                        value: AppFormatters.date(profile.joiningDate),
                      ),
                      InfoRow(
                        label: Strings.employmentType,
                        value: AppFormatters.enumLabel(profile.employmentType),
                      ),
                      InfoRow(
                        label: Strings.legalEntity,
                        value: profile.legalEntity ?? '-',
                      ),
                      InfoRow(
                        label: Strings.costCentre,
                        value: profile.costCentre ?? '-',
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                SectionCard(
                  title: 'Contact',
                  child: Column(
                    children: [
                      InfoRow(
                        label: Strings.phone,
                        value: profile.phone ?? Strings.noPhone,
                      ),
                      InfoRow(
                        label: Strings.personalEmail,
                        value: profile.personalEmail ?? '-',
                      ),
                      InfoRow(
                        label: Strings.address,
                        value: profile.address ?? '-',
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
