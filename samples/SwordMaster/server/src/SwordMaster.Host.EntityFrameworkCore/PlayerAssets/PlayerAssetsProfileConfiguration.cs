using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwordMaster.Domain.PlayerAssets;

namespace SwordMaster.Host.EntityFrameworkCore.PlayerAssets;

public sealed class PlayerAssetsProfileConfiguration :
    IEntityTypeConfiguration<PlayerAssetsProfile>
{
    private const string OrdinalCollation = "utf8mb4_0900_bin";

    public void Configure(EntityTypeBuilder<PlayerAssetsProfile> builder)
    {
        builder.ToTable("sm_player_assets_profiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id)
            .HasColumnName("PlayerId")
            .HasColumnType("char(36)")
            .ValueGeneratedNever();
        builder.Ignore(profile => profile.PlayerId);
        builder.Property(profile => profile.Revision)
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        ConfigureCharacter(builder);
        ConfigureOwnedItems(builder);
        ConfigureEquipmentAssignments(builder);
    }

    private static void ConfigureCharacter(EntityTypeBuilder<PlayerAssetsProfile> builder)
    {
        builder.OwnsOne(
            profile => profile.Character,
            character =>
            {
                character.ToTable("sm_player_characters");
                character.WithOwner().HasForeignKey("PlayerId");
                character.Property<Guid>("PlayerId").HasColumnType("char(36)");
                character.HasKey("PlayerId");
                character.Property(value => value.Id)
                    .HasColumnName("CharacterId")
                    .HasColumnType("char(36)")
                    .ValueGeneratedNever();
                character.Ignore(value => value.CharacterId);
                character.HasIndex(value => value.Id)
                    .IsUnique()
                    .HasDatabaseName("ux_sm_player_characters_character_id");

                character.OwnsMany(
                    value => value.BaseAttributes,
                    attributes =>
                    {
                        attributes.ToTable("sm_player_character_base_attributes");
                        attributes.WithOwner().HasForeignKey("PlayerId");
                        attributes.Property<Guid>("PlayerId").HasColumnType("char(36)");
                        attributes.Property(attribute => attribute.Code)
                            .HasConversion<StatCodeValueConverter>()
                            .HasColumnName("StatCode")
                            .HasMaxLength(StatCode.MaximumLength)
                            .UseCollation(OrdinalCollation)
                            .ValueGeneratedNever();
                        attributes.Property(attribute => attribute.Value)
                            .HasConversion<StatValueValueConverter>()
                            .HasColumnName("StatValue");
                        attributes.HasKey("PlayerId", nameof(AttributeValue.Code));
                    });
                character.Navigation(value => value.BaseAttributes)
                    .HasField("_baseAttributes")
                    .UsePropertyAccessMode(PropertyAccessMode.Field);
            });
        builder.Navigation(profile => profile.Character).IsRequired();
    }

    private static void ConfigureOwnedItems(EntityTypeBuilder<PlayerAssetsProfile> builder)
    {
        builder.OwnsMany(
            profile => profile.OwnedItems,
            items =>
            {
                items.ToTable("sm_owned_equipment_items");
                items.WithOwner().HasForeignKey("PlayerId");
                items.Property<Guid>("PlayerId").HasColumnType("char(36)");
                items.Property(item => item.Id)
                    .HasColumnName("OwnedItemId")
                    .HasColumnType("char(36)")
                    .ValueGeneratedNever();
                items.Ignore(item => item.OwnedItemId);
                items.Property(item => item.ConfiguredItemId)
                    .HasMaxLength(EquipmentDefinition.ConfiguredItemIdMaximumLength)
                    .UseCollation(OrdinalCollation)
                    .IsRequired();
                items.HasKey("PlayerId", nameof(OwnedEquipmentItem.Id));
            });
        builder.Navigation(profile => profile.OwnedItems)
            .HasField("_ownedItems")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureEquipmentAssignments(
        EntityTypeBuilder<PlayerAssetsProfile> builder)
    {
        builder.OwnsMany(
            profile => profile.EquipmentAssignments,
            assignments =>
            {
                assignments.ToTable("sm_equipment_assignments");
                assignments.WithOwner().HasForeignKey("PlayerId");
                assignments.Property<Guid>("PlayerId").HasColumnType("char(36)");
                assignments.Property(assignment => assignment.Id)
                    .HasColumnName("AssignmentId")
                    .HasColumnType("char(36)")
                    .ValueGeneratedNever();
                assignments.Property(assignment => assignment.CharacterId)
                    .HasConversion<CharacterIdValueConverter>()
                    .HasColumnType("char(36)")
                    .ValueGeneratedNever();
                assignments.Property(assignment => assignment.Slot)
                    .HasConversion<EquipmentSlotValueConverter>()
                    .HasMaxLength(EquipmentSlot.MaximumLength)
                    .UseCollation(OrdinalCollation)
                    .IsRequired();
                assignments.Property(assignment => assignment.OwnedItemId)
                    .HasConversion<OwnedItemIdValueConverter>()
                    .HasColumnType("char(36)")
                    .ValueGeneratedNever();
                assignments.Property(assignment => assignment.ConfiguredItemId)
                    .HasMaxLength(EquipmentDefinition.ConfiguredItemIdMaximumLength)
                    .UseCollation(OrdinalCollation)
                    .IsRequired();
                assignments.Property(assignment => assignment.ConfigurationVersionId)
                    .HasColumnType("char(36)")
                    .ValueGeneratedNever();
                var snapshotProperty = assignments
                    .Property(assignment => assignment.ModifierSnapshot)
                    .HasConversion<EquipmentModifierSnapshotJsonConverter>()
                    .HasColumnName("ModifierSnapshotJson")
                    .HasColumnType("longtext")
                    .IsRequired();
                snapshotProperty.Metadata.SetValueComparer(
                    new EquipmentModifierSnapshotValueComparer());
                assignments.Ignore(assignment => assignment.Modifiers);

                assignments.HasKey("PlayerId", nameof(EquipmentAssignment.Id));
                assignments.HasIndex("PlayerId", nameof(EquipmentAssignment.Slot))
                    .IsUnique()
                    .HasDatabaseName("ux_sm_equipment_assignments_player_slot");
                assignments.HasIndex("PlayerId", nameof(EquipmentAssignment.OwnedItemId))
                    .IsUnique()
                    .HasDatabaseName("ux_sm_equipment_assignments_player_item");
                assignments.HasIndex("PlayerId", nameof(EquipmentAssignment.CharacterId))
                    .HasDatabaseName("ix_sm_equipment_assignments_player_character");
            });
        builder.Navigation(profile => profile.EquipmentAssignments)
            .HasField("_equipmentAssignments")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
