using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Domain.Entities;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.ValueObjects;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class WorkflowWorkspaceConfiguration : IEntityTypeConfiguration<WorkflowWorkspace>
{
    public void Configure(EntityTypeBuilder<WorkflowWorkspace> builder)
    {
        builder.ToTable("WorkflowWorkspaces");

        builder.Property(w => w.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<WorkspaceId>());

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Workspace)
            .IsRequired();

        builder.Property(w => w.UpdatedAt);
    }
}
