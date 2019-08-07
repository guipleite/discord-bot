﻿// <auto-generated />
using System;
using CompatBot.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CompatBot.Migrations
{
    [DbContext(typeof(ThumbnailDb))]
    partial class ThumbnailDbModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "2.2.6-servicing-10079");

            modelBuilder.Entity("CompatBot.Database.State", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id");

                    b.Property<string>("Locale")
                        .HasColumnName("locale");

                    b.Property<long>("Timestamp")
                        .HasColumnName("timestamp");

                    b.HasKey("Id")
                        .HasName("id");

                    b.HasIndex("Locale")
                        .IsUnique()
                        .HasName("state_locale");

                    b.HasIndex("Timestamp")
                        .HasName("state_timestamp");

                    b.ToTable("state");
                });

            modelBuilder.Entity("CompatBot.Database.SyscallInfo", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id");

                    b.Property<string>("Function")
                        .IsRequired()
                        .HasColumnName("function");

                    b.Property<string>("Module")
                        .IsRequired()
                        .HasColumnName("module");

                    b.HasKey("Id")
                        .HasName("id");

                    b.HasIndex("Function")
                        .HasName("syscall_info_function");

                    b.HasIndex("Module")
                        .HasName("syscall_info_module");

                    b.ToTable("syscall_info");
                });

            modelBuilder.Entity("CompatBot.Database.SyscallToProductMap", b =>
                {
                    b.Property<int>("ProductId")
                        .HasColumnName("product_id");

                    b.Property<int>("SyscallInfoId")
                        .HasColumnName("syscall_info_id");

                    b.HasKey("ProductId", "SyscallInfoId")
                        .HasName("id");

                    b.HasIndex("SyscallInfoId")
                        .HasName("ix_syscall_to_product_map_syscall_info_id");

                    b.ToTable("syscall_to_product_map");
                });

            modelBuilder.Entity("CompatBot.Database.Thumbnail", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id");

                    b.Property<string>("ContentId")
                        .HasColumnName("content_id");

                    b.Property<string>("EmbeddableUrl")
                        .HasColumnName("embeddable_url");

                    b.Property<string>("Name")
                        .HasColumnName("name");

                    b.Property<string>("ProductCode")
                        .IsRequired()
                        .HasColumnName("product_code");

                    b.Property<long>("Timestamp")
                        .HasColumnName("timestamp");

                    b.Property<string>("Url")
                        .HasColumnName("url");

                    b.HasKey("Id")
                        .HasName("id");

                    b.HasIndex("ContentId")
                        .IsUnique()
                        .HasName("thumbnail_content_id");

                    b.HasIndex("ProductCode")
                        .IsUnique()
                        .HasName("thumbnail_product_code");

                    b.HasIndex("Timestamp")
                        .HasName("thumbnail_timestamp");

                    b.ToTable("thumbnail");
                });

            modelBuilder.Entity("CompatBot.Database.TitleInfo", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnName("id");

                    b.Property<string>("ContentId")
                        .IsRequired()
                        .HasColumnName("content_id");

                    b.Property<int?>("EmbedColor")
                        .HasColumnName("embed_color");

                    b.Property<string>("ThumbnailEmbeddableUrl")
                        .HasColumnName("thumbnail_embeddable_url");

                    b.Property<string>("ThumbnailUrl")
                        .HasColumnName("thumbnail_url");

                    b.Property<long>("Timestamp")
                        .HasColumnName("timestamp");

                    b.HasKey("Id")
                        .HasName("id");

                    b.HasIndex("ContentId")
                        .IsUnique()
                        .HasName("title_info_content_id");

                    b.HasIndex("Timestamp")
                        .HasName("title_info_timestamp");

                    b.ToTable("title_info");
                });

            modelBuilder.Entity("CompatBot.Database.SyscallToProductMap", b =>
                {
                    b.HasOne("CompatBot.Database.Thumbnail", "Product")
                        .WithMany("SyscallToProductMap")
                        .HasForeignKey("ProductId")
                        .HasConstraintName("fk_syscall_to_product_map__thumbnail_product_id")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("CompatBot.Database.SyscallInfo", "SyscallInfo")
                        .WithMany("SyscallToProductMap")
                        .HasForeignKey("SyscallInfoId")
                        .HasConstraintName("fk_syscall_to_product_map_syscall_info_syscall_info_id")
                        .OnDelete(DeleteBehavior.Cascade);
                });
#pragma warning restore 612, 618
        }
    }
}
