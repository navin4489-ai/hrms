import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/strings.dart';
import '../../core/utils/formatters.dart';
import '../../providers/profile_provider.dart';
import '../../widgets/app_scaffold.dart';
import '../../widgets/async_body.dart';
import '../../widgets/section_card.dart';
import '../../widgets/status_chip.dart';

/// My profile (FR-EMP-020) with links to edit, sensitive changes,
/// employment history, sessions and password change.
class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final me = ref.watch(meProvider);
    final theme = Theme.of(context);

    return AppScaffold(
      title: Strings.profile,
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(meProvider),
        child: AsyncBody(
          value: me,
          onRetry: () => ref.invalidate(meProvider),
          builder: (profile) => SingleChildScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    CircleAvatar(
                      radius: 32,
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
                              [
                                profile.designation!,
                                if (profile.department != null)
                                  profile.department!,
                              ].join(' · '),
                              style: theme.textTheme.bodyMedium?.copyWith(
                                color: theme.colorScheme.onSurfaceVariant,
                              ),
                            ),
                          const SizedBox(height: 4),
                          if (profile.employmentStatus != null)
                            StatusChip(
                              status: profile.employmentStatus,
                              dense: true,
                            ),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                SectionCard(
                  title: 'Work details',
                  child: Column(
                    children: [
                      InfoRow(
                        label: 'Employee number',
                        value: profile.employeeNumber,
                      ),
                      InfoRow(
                        label: Strings.workEmail,
                        value: profile.workEmail ?? '-',
                      ),
                      InfoRow(
                        label: Strings.designation,
                        value: profile.designation ?? '-',
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
                        label: Strings.manager,
                        value: profile.managerName ?? '-',
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
                  title: 'Personal details',
                  child: Column(
                    children: [
                      InfoRow(
                        label: Strings.preferredName,
                        value: profile.preferredName ?? '-',
                      ),
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
                      InfoRow(
                        label: Strings.dateOfBirth,
                        value: AppFormatters.date(profile.dateOfBirth),
                      ),
                      InfoRow(
                        label: Strings.gender,
                        value: AppFormatters.enumLabel(profile.gender),
                      ),
                      InfoRow(
                        label: Strings.emergencyContactName,
                        value: profile.emergencyContactName ?? '-',
                      ),
                      InfoRow(
                        label: Strings.emergencyContactPhone,
                        value: profile.emergencyContactPhone ?? Strings.noPhone,
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                SectionCard(
                  title: 'Manage',
                  padding: const EdgeInsets.symmetric(
                    horizontal: 8,
                    vertical: 12,
                  ),
                  child: Column(
                    children: [
                      _ActionTile(
                        icon: Icons.edit_outlined,
                        label: Strings.editProfile,
                        onTap: () => context.go('/more/profile/edit'),
                      ),
                      _ActionTile(
                        icon: Icons.verified_user_outlined,
                        label: Strings.sensitiveChange,
                        onTap: () => context.go('/more/profile/sensitive'),
                      ),
                      _ActionTile(
                        icon: Icons.history_edu_rounded,
                        label: Strings.employmentHistory,
                        onTap: () => context.go('/more/profile/history'),
                      ),
                      _ActionTile(
                        icon: Icons.devices_outlined,
                        label: Strings.sessions,
                        onTap: () => context.go('/more/profile/sessions'),
                      ),
                      _ActionTile(
                        icon: Icons.lock_outline_rounded,
                        label: Strings.changePassword,
                        onTap: () =>
                            context.go('/more/profile/change-password'),
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

class _ActionTile extends StatelessWidget {
  const _ActionTile({
    required this.icon,
    required this.label,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      dense: true,
      contentPadding: EdgeInsets.zero,
      leading: Icon(icon),
      title: Text(label),
      trailing: const Icon(Icons.chevron_right_rounded, size: 20),
      onTap: onTap,
    );
  }
}
